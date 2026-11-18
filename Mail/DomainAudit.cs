using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;

namespace mailTester.Mail
{
    public sealed partial class DnsDiagnosticEngine
    {
        public async Task<IReadOnlyList<CheckFinding>> AuditDomainAsync(string domain, string[] selectors, IProgress<string> progress, CancellationToken ct)
        {
            domain = DnsPolicyChecks.NormalizeDomain(domain);
            if (!domain.Contains('.')) throw new ArgumentException("Укажите зарегистрированное доменное имя.");
            var rows = (await InspectAsync(domain, selectors, progress, ct).ConfigureAwait(false)).Findings.ToList();

            progress?.Report("Регистрационные сведения и TLS сайта");
            var registration = RegistrationDiagnostics.InspectAsync(domain, progress, ct);
            var website = TlsDiagnostics.InspectAsync(domain, 443, TlsMode.Https, ct);
            var www = "www." + domain;
            var wwwIps = await Task.WhenAll(Query(www, QueryType.A, ct), Query(www, QueryType.AAAA, ct)).ConfigureAwait(false);
            Task<IReadOnlyList<CheckFinding>> wwwTls = wwwIps.Any(x => !x.Failed && Addresses(x).Length > 0)
                ? TlsDiagnostics.InspectAsync(www, 443, TlsMode.Https, ct)
                : Task.FromResult<IReadOnlyList<CheckFinding>>(new[] { new CheckFinding("TLS", www, CheckLevel.Skipped, wwwIps.Any(x => x.Failed) ? "DNS www не проверен" : "www не разрешается", "HTTPS не проверялся.") });
            await Task.WhenAll(registration, website, wwwTls).ConfigureAwait(false);
            rows.AddRange(await registration.ConfigureAwait(false));
            rows.AddRange(await website.ConfigureAwait(false));
            rows.AddRange(await wwwTls.ConfigureAwait(false));

            progress?.Report("Проверка MX: SMTP STARTTLS, PTR/FCrDNS");
            var mx = await Query(domain, QueryType.MX, ct).ConfigureAwait(false);
            var hosts = mx.Records.OfType<MxRecord>().OrderBy(x => x.Preference)
                .Select(x => x.Exchange.Value.TrimEnd('.')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var allIps = new HashSet<System.Net.IPAddress>();
            foreach (var host in hosts.Take(5))
            {
                // MX supports receiving SMTP; these addresses need not be the outbound MTA addresses.
                var addresses = await Task.WhenAll(Query(host, QueryType.A, ct), Query(host, QueryType.AAAA, ct)).ConfigureAwait(false);
                foreach (var ip in addresses.Where(x => !x.Failed).SelectMany(Addresses)) allIps.Add(ip);
                var cert = await TlsDiagnostics.InspectAsync(host, 25, TlsMode.SmtpStartTls, ct).ConfigureAwait(false);
                rows.AddRange(cert);
            }
            foreach (var ip in allIps.Take(10))
                rows.AddRange(await CheckIpAsync(ip, false, progress, ct).ConfigureAwait(false));
            if (hosts.Length > 5 || allIps.Count > 10)
                rows.Add(new("MX", domain, CheckLevel.Skipped, "Лимит комплексного аудита", "TLS: до 5 MX; PTR: до 10 адресов."));
            rows.Add(new("DNSBL", domain, CheckLevel.Info, "Для DNSBL укажите исходящий SMTP IP отдельно", "Входящие MX не обязательно отправляют почту. Автоматически не передаём адреса MX сторонним спискам."));
            return rows;
        }
    }
}
