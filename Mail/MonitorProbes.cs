using DnsClient;
using DnsClient.Protocol;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace mailTester.Mail;

public static partial class MailEngine
{
    public static async Task ProbeServiceAsync(MailConnection connection, string protocol, CancellationToken ct)
    {
        connection.Validate(false);
        if (connection.Security == SecureSocketOptions.None) throw new ArgumentException("TLS required");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        using MailService client = protocol switch {
            "Smtp" => new SmtpClient(), "Pop3" => new Pop3Client(), "Imap" => new ImapClient(), _ => throw new ArgumentException("Protocol")
        };
        await ConnectAsync(client, connection, null, deadline.Token).ConfigureAwait(false);
        if (client is ImapClient imap && client.IsAuthenticated) await imap.Inbox.OpenAsync(FolderAccess.ReadOnly, deadline.Token).ConfigureAwait(false);
        await client.DisconnectAsync(false, deadline.Token).ConfigureAwait(false);
    }
}

public sealed partial class DnsDiagnosticEngine
{
    public async Task<IReadOnlyList<CheckFinding>> CheckMailDnsAsync(string domain, CancellationToken ct)
    {
        domain = DnsPolicyChecks.NormalizeDomain(domain);
        var replies = await Task.WhenAll(Query(domain,QueryType.A,ct),Query(domain,QueryType.AAAA,ct),Query(domain,QueryType.MX,ct)).ConfigureAwait(false);
        var result = replies.Select(x => Raw(x.Type == QueryType.MX ? "MX" : "DNS",x)).ToList();
        var mx = replies[2];
        var hosts = mx.Records.OfType<MxRecord>().ToArray();
        if (mx.Failed || mx.Code == 3) result.Add(new("MX",domain,mx.Failed?CheckLevel.Unknown:CheckLevel.Error,mx.Failed?"MX DNS не проверен":"MX: NXDOMAIN",mx.Text));
        else if (hosts.Length == 0)
            result.Add(new("MX",domain,replies.Take(2).Any(x => !x.Failed && Addresses(x).Length > 0) ? CheckLevel.Warning : CheckLevel.Error,
                "Нет явного MX","Проверьте implicit MX и назначение домена."));
        else if (hosts.Any(x => x.Exchange.Value.TrimEnd('.').Length == 0))
            result.Add(new("MX",domain,hosts.Length == 1 && hosts[0].Preference == 0 ? CheckLevel.Warning : CheckLevel.Error,"Null MX: домен не принимает почту",mx.Text));
        else
            foreach (var host in hosts.Select(x=>x.Exchange.Value).Distinct(StringComparer.OrdinalIgnoreCase).Take(10))
            {
                var a = await Task.WhenAll(Query(host,QueryType.A,ct),Query(host,QueryType.AAAA,ct)).ConfigureAwait(false);
                result.Add(new("MX",host,a.Any(x=>x.Failed) ? CheckLevel.Warning : a.Any(x=>Addresses(x).Length>0) ? CheckLevel.Ok : CheckLevel.Error,
                    "Адреса MX",string.Join("\n",a.Select(x=>x.Text))));
            }
        return result;
    }
}
