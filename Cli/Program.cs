using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using mailTester.Mail;
using mailTester.Monitoring;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private sealed class Args
    {
        public List<string> Positionals { get; } = new();
        private readonly Dictionary<string,string> options = new(StringComparer.OrdinalIgnoreCase);
        public Args(string[] args)
        {
            for (int i=0;i<args.Length;i++)
            {
                string value = args[i];
                if (!value.StartsWith("--",StringComparison.Ordinal)) { Positionals.Add(value); continue; }
                string key = value[2..];
                if (key == "json") { if (!options.TryAdd("format","json")) throw new ArgumentException("Duplicate format"); continue; }
                if (key == "help") { if (!options.TryAdd(key,"true")) throw new ArgumentException("Duplicate option"); continue; }
                if (i+1 >= args.Length || args[i+1].StartsWith("--",StringComparison.Ordinal)) throw new ArgumentException("Missing option value");
                if (!options.TryAdd(key,args[++i])) throw new ArgumentException("Duplicate option");
            }
        }
        public string Get(string name, string fallback = null) => options.TryGetValue(name,out var value) ? value : fallback;
        public string Require(string name) => Get(name) ?? throw new ArgumentException("Missing option: " + name);
        public int Int(string name,int fallback,int min,int max)
        {
            if (!int.TryParse(Get(name,fallback.ToString(CultureInfo.InvariantCulture)),out int result) || result<min || result>max)
                throw new ArgumentException("Invalid numeric option: " + name);
            return result;
        }
        public string Pos(int index) => index<Positionals.Count ? Positionals[index] : throw new ArgumentException("Missing argument");
        public void Check(int count,params string[] allowed)
        {
            if (Positionals.Count != count || options.Keys.Any(x => x!="format" && !allowed.Contains(x,StringComparer.OrdinalIgnoreCase)))
                throw new ArgumentException("Unrecognized argument or option");
        }
    }

    private static void Help()
    {
        Console.WriteLine("""
MailTester CLI 1.6
  dns <domain> [--resolver IP]
  dns-records <owner> [--resolver IP]
  spf <domain> [--ip IP --from ADDRESS --helo DOMAIN] [--resolver IP]
  dmarc <domain> [--resolver IP]
  dkim <domain> --selector selector1,selector2 [--resolver IP]
  whois <domain>
  ptr <IP> [--resolver IP]
  blacklist <IPv4> [--resolver IP]
  tls <host> [--mode Https|SmtpStartTls|SmtpImplicit|Pop3StartTls|Pop3Implicit|ImapStartTls|ImapImplicit] [--port N]
  audit <domain> [--selector selector1,selector2] [--resolver IP]
  eml <file> [--ip IP --from ADDRESS --helo DOMAIN] [--resolver IP]
  monitor list [--db PATH]
  monitor history <profile-id> [--limit N] [--db PATH]
  monitor run <profile-id> [--db PATH]
  monitor run-due [--max N] [--db PATH]
  monitor status <profile-id> [--db PATH]
  roundtrip <profile-id> [--db PATH]

  --format text|json|csv|zabbix     (--json is a shortcut)
  zabbix prints a numeric check result: 0 OK, 1 Warning, 2 Error, 3 Unknown;
          returns exit 0 for a valid check result, 3 for argument/runtime errors.
  Standard exit codes: 0 OK, 1 Warning, 2 Error, 3 Unknown/invalid/cancelled.
  Create/edit encrypted profiles with the GUI under the same Windows user.
  Roundtrip uses a saved profile; never pass passwords on the command line.
  A monitor run sends a real message only for an explicitly configured RoundTrip profile.
  Monitor run-due runs enabled, due profiles and records results; no daemon.
""");
    }

    private static IPAddress Ip(string text)
    {
        if (!IPAddress.TryParse(text,out var ip)) throw new ArgumentException("Invalid IP");
        return ip;
    }
    private static string Q(object value) => "\"" + (Convert.ToString(value,CultureInfo.InvariantCulture) ?? "").Replace("\",""\"\"") + "\"";
    private static int Code(MonitorStatus status) => status switch { MonitorStatus.Ok=>0, MonitorStatus.Warning=>1, MonitorStatus.Error=>2, _=>3 };
    private static MonitorStatus Worst(IEnumerable<MonitorStatus> statuses) =>
        statuses.DefaultIfEmpty(MonitorStatus.Ok).OrderByDescending(Code).First();

    private static int Findings(IReadOnlyList<CheckFinding> items,string format)
    {
        var status = MonitorRunner.StatusOf(items);
        if (format=="zabbix") { Console.WriteLine(Code(status)); return 0; }
        if (format=="json") Console.WriteLine(JsonSerializer.Serialize(new { status, findings=items },Json));
        else if (format=="csv")
        {
            Console.WriteLine("section,name,level,summary,detail,recommendation");
            foreach(var f in items) Console.WriteLine(string.Join(",",Q(f.Section),Q(f.Name),Q(f.Level),Q(f.Summary),Q(f.Detail),Q(DiagnosticAdvice.For(f))));
        }
        else foreach(var f in items) Console.WriteLine($"[{f.Level}] {f.Section} {f.Name}\n{f.Summary}\n{f.Detail}\nAdvice: {DiagnosticAdvice.For(f)}\n");
        return Code(status);
    }

    private static int Runs(IReadOnlyList<MonitorRun> runs,string format,bool emptyOk=false)
    {
        var status = runs.Count==0 && !emptyOk ? MonitorStatus.Unknown : Worst(runs.Select(r=>r.Status));
        if (format=="zabbix") { Console.WriteLine(Code(status)); return 0; }
        if (format=="json") Console.WriteLine(JsonSerializer.Serialize(runs,Json));
        else if (format=="csv")
        {
            Console.WriteLine("job_id,name,started_utc,status,seconds,delivery_seconds,summary,detail");
            foreach(var r in runs) Console.WriteLine(string.Join(",",Q(r.JobId),Q(r.Name),Q(r.Started.ToString("O")),Q(r.Status),Q(r.Seconds),Q(r.DeliverySeconds),Q(r.Summary),Q(r.Detail)));
        }
        else
        {
            if (runs.Count==0) Console.WriteLine("No runs.");
            foreach(var r in runs) Console.WriteLine($"{r.Started:O} [{r.Status}] {r.Name} ({r.Seconds:F2}s) delivery={r.DeliverySeconds?.ToString("F2",CultureInfo.InvariantCulture) ?? "-"}\n{r.Summary}\n{r.Detail}\n");
        }
        return Code(status);
    }

    private static async Task<int> MonitorAsync(Args a,string format,CancellationToken ct)
    {
        string command = a.Pos(1).ToLowerInvariant();
        if (command=="list") a.Check(2,"db");
        else if (command=="run-due") a.Check(2,"db","max");
        else if (command=="history") a.Check(3,"db","limit");
        else if (command is "run" or "status") a.Check(3,"db");
        else throw new ArgumentException("Unknown monitor command");
        var db = new MonitorStore(a.Get("db"));
        var runner = new MonitorRunner(db);
        if (command=="list")
        {
            if (format=="zabbix") throw new ArgumentException("Use monitor status <id> for Zabbix");
            var items=db.Entries().Select(x=>new { id=x.Job.Id,name=x.Job.Name,kind=x.Job.Kind,target=x.Job.Target,enabled=x.Job.Enabled,nextDue=x.NextDue,busy=x.Busy,last=x.Last?.Status }).ToList();
            if (format=="json") Console.WriteLine(JsonSerializer.Serialize(items,Json));
            else if(format=="csv")
            {
                Console.WriteLine("id,name,kind,target,enabled,next_due,busy,last");
                foreach(var x in items) Console.WriteLine(string.Join(",",Q(x.id),Q(x.name),Q(x.kind),Q(x.target),Q(x.enabled),Q(x.nextDue.ToString("O")),Q(x.busy),Q(x.last)));
            }
            else foreach(var x in items) Console.WriteLine($"{x.id} | {x.name} | {x.kind} | {x.target} | enabled={x.enabled} | {x.last?.ToString() ?? "Never"}");
            return 0;
        }
        if (command=="run-due") return Runs(await runner.RunDueAsync(a.Int("max",20,1,100),null,ct),format,true);
        string id=a.Pos(2);
        if (!db.Entries().Any(x=>x.Job.Id==id)) throw new ArgumentException("Unknown monitor profile");
        if (command=="history" || command=="status") return Runs(db.History(id,command=="status"?1:a.Int("limit",100,1,10000)),format);
        var claim=db.Claim(id);
        if (claim==null) throw new InvalidOperationException("Profile busy");
        return Runs(new[]{await runner.RunClaimAsync(claim,null,ct)},format);
    }

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding=Encoding.UTF8;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var cancel=new CancellationTokenSource();
        Console.CancelKeyPress += (_,e)=>{e.Cancel=true;cancel.Cancel();};
        try
        {
            var a=new Args(args);
            if(a.Positionals.Count==0 || a.Get("help")!=null || a.Pos(0)=="help"){Help();return 0;}
            string format=a.Get("format","text").ToLowerInvariant();
            if(format is not ("text" or "json" or "csv" or "zabbix")) throw new ArgumentException("Invalid format");
            string command=a.Pos(0).ToLowerInvariant();
            if(command=="monitor") return await MonitorAsync(a,format,cancel.Token);
            if(command=="roundtrip")
            {
                a.Check(2,"db");
                var db=new MonitorStore(a.Get("db"));
                var entry=db.Entries().SingleOrDefault(x=>x.Job.Id==a.Pos(1));
                if(entry==null || entry.Job.Kind!=MonitorKind.RoundTrip) throw new ArgumentException("RoundTrip profile required");
                var claim=db.Claim(entry.Job.Id);
                if(claim==null) throw new InvalidOperationException("Profile busy");
                return Runs(new[]{await new MonitorRunner(db).RunClaimAsync(claim,null,cancel.Token)},format);
            }
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            deadline.CancelAfter(TimeSpan.FromMinutes(5));
            var ct=deadline.Token;
            string target=a.Pos(1);
            IPAddress resolver=a.Get("resolver")==null?null:Ip(a.Get("resolver"));
            var engine=new DnsDiagnosticEngine(resolver);
            IReadOnlyList<CheckFinding> findings;
            switch(command)
            {
                case "dns":
                    a.Check(2,"resolver"); findings=await engine.CheckMailDnsAsync(target,ct);break;
                case "dns-records":
                    a.Check(2,"resolver");findings=await engine.LookupRecordsAsync(target,ct);break;
                case "spf":
                    a.Check(2,"resolver","ip","from","helo");
                    if(a.Get("ip")!=null)
                    {
                        string sender=a.Require("from");
                        // The domain is the tested SMTP identity, not the visible From header.
                        string d=sender=="<>"?a.Require("helo"):sender.Trim('<','>').Split('@').Last();
                        if(DnsPolicyChecks.NormalizeDomain(d)!=DnsPolicyChecks.NormalizeDomain(target)) throw new ArgumentException("SPF identity and domain differ");
                        findings=await engine.CheckSpfAsync(Ip(a.Require("ip")),sender,a.Get("helo"),ct);
                    }
                    else
                    {
                        if(a.Get("from")!=null || a.Get("helo")!=null) throw new ArgumentException("SPF IP required");
                        findings=(await engine.InspectAsync(target,Array.Empty<string>(),null,ct)).Findings.Where(f=>f.Section=="SPF").ToArray();
                    }
                    break;
                case "dmarc":
                    a.Check(2,"resolver");findings=(await engine.InspectAsync(target,Array.Empty<string>(),null,ct)).Findings.Where(f=>f.Section=="DMARC").ToArray();break;
                case "dkim":
                    a.Check(2,"resolver","selector");
                    var selectors=DnsPolicyChecks.ParseSelectors(a.Require("selector"));
                    if(selectors.Length==0) throw new ArgumentException("Selector required");
                    findings=(await engine.InspectAsync(target,selectors,null,ct)).Findings.Where(f=>f.Section=="DKIM").ToArray();break;
                case "whois":
                    a.Check(2);findings=await RegistrationDiagnostics.InspectAsync(target,null,ct);break;
                case "ptr":
                case "blacklist":
                    a.Check(2,"resolver");findings=await engine.CheckIpAsync(Ip(target),command=="blacklist",null,ct);
                    findings=findings.Where(f=>f.Section==(command=="blacklist"?"DNSBL":"PTR")).ToArray();break;
                case "tls":
                    a.Check(2,"mode","port");
                    if(!Enum.TryParse<TlsMode>(a.Get("mode","Https"),true,out var mode) || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid TLS mode");
                    findings=await TlsDiagnostics.InspectAsync(target,a.Int("port",TlsDiagnostics.DefaultPort(mode),1,65535),mode,ct);break;
                case "audit":
                    a.Check(2,"resolver","selector");findings=await engine.AuditDomainAsync(target,DnsPolicyChecks.ParseSelectors(a.Get("selector","")),null,ct);break;
                case "eml":
                    a.Check(2,"resolver","ip","from","helo");findings=await engine.AnalyzeEmlAsync(target,a.Get("ip")==null?null:Ip(a.Get("ip")),a.Get("from"),a.Get("helo"),null,ct);break;
                default:throw new ArgumentException("Unknown command");
            }
            return Findings(findings,format);
        }
        catch(OperationCanceledException){Console.Error.WriteLine("Cancelled or timeout.");return 3;}
        catch(Exception ex){Console.Error.WriteLine("MailTester: "+ex.GetType().Name+". See --help; check parameters and profile access.");return 3;}
    }
}
