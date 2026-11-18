using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace mailTester.Monitoring;

public sealed class MonitorStore
{
    private readonly string connectionString;
    public string PathName { get; }
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MailTester", "monitor.db");

    public MonitorStore(string path = null)
    {
        PathName = Path.GetFullPath(path ?? DefaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = PathName, DefaultTimeout = 5, Pooling = false }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS jobs(
                id TEXT PRIMARY KEY, payload TEXT NOT NULL, enabled INTEGER NOT NULL,
                due INTEGER NOT NULL, lease_token TEXT, lease_until INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS runs(
                run_id INTEGER PRIMARY KEY, job_id TEXT NOT NULL, started INTEGER NOT NULL,
                payload TEXT NOT NULL, FOREIGN KEY(job_id) REFERENCES jobs(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS runs_job_started ON runs(job_id,started DESC,run_id DESC);
            CREATE INDEX IF NOT EXISTS jobs_due ON jobs(enabled,due);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
        return db;
    }
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static void Param(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static T Decode<T>(string json) where T : class => JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Invalid monitor data");
    private static string Encode<T>(T item) => JsonSerializer.Serialize(item);

    public void Save(MonitorJob job)
    {
        job.Validate();
        // Editing does not clear another process's live lease; all changes to running jobs are refused.
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO jobs(id,payload,enabled,due) VALUES($id,$payload,$enabled,$due)
            ON CONFLICT(id) DO UPDATE SET payload=excluded.payload,enabled=excluded.enabled,due=excluded.due
            WHERE jobs.lease_until <= $now;
            """;
        Param(cmd,"$id",job.Id); Param(cmd,"$payload",Encode(job)); Param(cmd,"$enabled",job.Enabled ? 1 : 0);
        Param(cmd,"$due",Now); Param(cmd,"$now",Now);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Профиль выполняется в другом процессе.");
    }

    public bool Delete(string id)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM jobs WHERE id=$id AND lease_until <= $now;";
        Param(cmd,"$id",id); Param(cmd,"$now",Now);
        return cmd.ExecuteNonQuery() == 1;
    }

    public IReadOnlyList<MonitorEntry> Entries()
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT j.payload,j.due,j.lease_until,
               (SELECT r.payload FROM runs r WHERE r.job_id=j.id ORDER BY r.started DESC,r.run_id DESC LIMIT 1)
            FROM jobs j ORDER BY j.due,j.id;
            """;
        using var reader = cmd.ExecuteReader();
        var rows = new List<MonitorEntry>();
        long now = Now;
        while (reader.Read())
            rows.Add(new(Decode<MonitorJob>(reader.GetString(0)), DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)),
                reader.GetInt64(2) > now, reader.IsDBNull(3) ? null : Decode<MonitorRun>(reader.GetString(3))));
        return rows;
    }

    public IReadOnlyList<MonitorRun> History(string id, int limit = 100)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM runs WHERE job_id=$id ORDER BY started DESC,run_id DESC LIMIT $limit;";
        Param(cmd,"$id",id); Param(cmd,"$limit",Math.Clamp(limit,1,10000));
        using var reader = cmd.ExecuteReader();
        var rows = new List<MonitorRun>();
        while (reader.Read()) rows.Add(Decode<MonitorRun>(reader.GetString(0)));
        return rows;
    }

    // Atomic cross-process claim. Due jobs are leased; no overlapping scheduler sends.
    // A crashed process leaves a lease until expiration, not a permanent lock.
    public MonitorClaim Claim(string id = null)
    {
        string token = Guid.NewGuid().ToString("N");
        long now = Now;
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = id == null ? """
            UPDATE jobs SET lease_token=$token,lease_until=$until
            WHERE id=(SELECT id FROM jobs WHERE enabled=1 AND due <= $now AND lease_until <= $now ORDER BY due,id LIMIT 1)
            AND lease_until <= $now
            RETURNING payload;
            """ : """
            UPDATE jobs SET lease_token=$token,lease_until=$until
            WHERE id=$id AND lease_until <= $now RETURNING payload;
            """;
        Param(cmd,"$token",token); Param(cmd,"$until",now+600); Param(cmd,"$now",now);
        if (id != null) Param(cmd,"$id",id);
        var json = cmd.ExecuteScalar() as string;
        return json == null ? null : new MonitorClaim(Decode<MonitorJob>(json),token);
    }

    public void Complete(MonitorClaim claim, MonitorRun run)
    {
        if (claim.Job.Id != run.JobId) throw new ArgumentException("Run job mismatch.");
        using var db = Open(); using var tx = db.BeginTransaction();
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE jobs SET lease_token=NULL,lease_until=0,due=$due WHERE id=$id AND lease_token=$token;
                """;
            Param(cmd,"$id",claim.Job.Id); Param(cmd,"$token",claim.Token); Param(cmd,"$due",Now+claim.Job.IntervalMinutes*60L);
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Monitor lease lost.");
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO runs(job_id,started,payload) VALUES($id,$started,$payload);";
            Param(cmd,"$id",run.JobId); Param(cmd,"$started",run.Started.ToUnixTimeSeconds()); Param(cmd,"$payload",Encode(run));
            cmd.ExecuteNonQuery();
        }
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM runs WHERE started < $cutoff;";
            Param(cmd,"$cutoff",Now-90L*86400);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
