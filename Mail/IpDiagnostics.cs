using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;

namespace mailTester.Mail
{
    public sealed partial class DnsDiagnosticEngine
    {
        public static string ReverseName(IPAddress ip)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            return ip.AddressFamily == AddressFamily.InterNetwork
                ? string.Join(".", ip.GetAddressBytes().Reverse()) + ".in-addr.arpa"
                : string.Join(".", Convert.ToHexString(ip.GetAddressBytes()).ToLowerInvariant().Reverse()) + ".ip6.arpa";
        }

        private static IPAddress[] Addresses(Answer answer) => answer.Records.OfType<ARecord>().Select(x => x.Address)
            .Concat(answer.Records.OfType<AaaaRecord>().Select(x => x.Address)).Distinct().ToArray();

        public async Task<IReadOnlyList<CheckFinding>> CheckIpAsync(IPAddress ip, bool dnsbl, IProgress<string> progress, CancellationToken ct)
        {
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var rows = new List<CheckFinding>();
            progress?.Report("PTR / forward-confirmed reverse DNS");
            var ptr = await Query(ReverseName(ip), QueryType.PTR, ct).ConfigureAwait(false);
            var names = ptr.Records.OfType<PtrRecord>().Select(x => x.PtrDomainName.Value).Distinct().ToArray();
            rows.Add(new("PTR", ip.ToString(), ptr.Failed ? CheckLevel.Error : names.Length > 0 ? CheckLevel.Ok : CheckLevel.Warning,
                ptr.Failed ? "Ошибка DNS" : names.Length == 0 ? "PTR отсутствует" : "PTR: " + string.Join(", ", names), ptr.Text));
            foreach (string name in names.Take(10))
            {
                var forward = await Query(name, ip.AddressFamily == AddressFamily.InterNetwork ? QueryType.A : QueryType.AAAA, ct).ConfigureAwait(false);
                bool match = Addresses(forward).Contains(ip);
                rows.Add(new("PTR", name, forward.Failed ? CheckLevel.Warning : match ? CheckLevel.Ok : CheckLevel.Warning,
                    forward.Failed ? "FCrDNS не определён" : match ? "FCrDNS совпадает" : "FCrDNS не совпадает", forward.Text));
            }
            if (names.Length > 10) rows.Add(new("PTR", ip.ToString(), CheckLevel.Skipped, "Лимит 10 PTR", ""));
            if (!dnsbl) return rows;
            if (ip.AddressFamily != AddressFamily.InterNetwork)
            {
                rows.Add(new("DNSBL", ip.ToString(), CheckLevel.Skipped, "DNSBL в этой версии только IPv4", ""));
                return rows;
            }
            var bytes = ip.GetAddressBytes();
            if (IPAddress.IsLoopback(ip) || bytes[0] == 0 || bytes[0] == 10 || bytes[0] >= 224 ||
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254) ||
                (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127))
            {
                rows.Add(new("DNSBL", ip.ToString(), CheckLevel.Skipped, "Непубличный IP: DNSBL не запрашивался", ""));
                return rows;
            }
            progress?.Report("DNSBL: Spamhaus ZEN и SpamCop");
            string reversed = string.Join(".", bytes.Reverse());
            foreach (string zone in new[] { "zen.spamhaus.org", "bl.spamcop.net" })
            {
                bool Listed(IPAddress addr)
                {
                    string code = addr.ToString();
                    return zone == "zen.spamhaus.org"
                        ? new[] { "127.0.0.2", "127.0.0.3", "127.0.0.4", "127.0.0.5", "127.0.0.6", "127.0.0.7", "127.0.0.9", "127.0.0.10", "127.0.0.11" }.Contains(code)
                        : code == "127.0.0.2";
                }
                var answers = await Task.WhenAll(Query("2.0.0.127." + zone, QueryType.A, ct),
                    Query("1.0.0.127." + zone, QueryType.A, ct), Query(reversed + "." + zone, QueryType.A, ct)).ConfigureAwait(false);
                var result = answers[2];
                var codes = Addresses(result);
                bool controlsOk = !answers[0].Failed && !answers[1].Failed && Addresses(answers[0]).Any(Listed) &&
                    Addresses(answers[1]).Length == 0;
                string detail = $"IP: {ip}\nОтвет: {result.Text}\nКонтроль +: {answers[0].Text}\nКонтроль -: {answers[1].Text}\nУчитывайте условия использования списка и ограничения публичного резолвера. PBL не обязательно означает спам.";
                if (result.Failed || !controlsOk || codes.Any(x => !Listed(x)))
                    rows.Add(new("DNSBL", zone, CheckLevel.Warning, "UNKNOWN: список/резолвер не подтверждён", detail));
                else if (codes.Any(Listed))
                    rows.Add(new("DNSBL", zone, CheckLevel.Warning, "LISTED: " + string.Join(", ", codes.Select(x => x.ToString())), detail));
                else rows.Add(new("DNSBL", zone, CheckLevel.Ok, "NOT LISTED на момент запроса", detail));
            }
            return rows;
        }

        public async Task<IReadOnlyList<CheckFinding>> LookupRecordsAsync(string name, CancellationToken ct)
        {
            string[] labels = (name ?? "").Trim().TrimEnd('.').Split('.');
            if (labels.Any(x => x.Length == 0)) throw new ArgumentException("Некорректное имя записи.");
            string owner = string.Join(".", labels.Select(x => {
                if (x.StartsWith("_"))
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(x, @"^_[a-zA-Z0-9_-]{1,62}$")) throw new ArgumentException("Некорректное имя записи.");
                    return x;
                }
                var ascii = new System.Globalization.IdnMapping { UseStd3AsciiRules = true }.GetAscii(x).ToLowerInvariant();
                if (!System.Text.RegularExpressions.Regex.IsMatch(ascii, @"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")) throw new ArgumentException("Некорректное имя записи.");
                return ascii;
            }));
            if (owner.Length > 253) throw new ArgumentException("Слишком длинное имя DNS.");
            var types = new[] { QueryType.A, QueryType.AAAA, QueryType.CNAME, QueryType.MX, QueryType.NS, QueryType.SOA, QueryType.TXT,
                QueryType.PTR, QueryType.SRV, QueryType.CAA, QueryType.DS, QueryType.DNSKEY, QueryType.TLSA, QueryType.SSHFP,
                QueryType.NAPTR, (QueryType)256, QueryType.CERT };
            var replies = await Task.WhenAll(types.Select(t => Query(owner, t, ct))).ConfigureAwait(false);
            return replies.Select(x => Raw("DNS", x)).Append(new("DNS", owner, CheckLevel.Info, "Запросы известных типов", "Записи других имён/поддоменов не перечисляются. DNSSEC не валидируется.")).ToArray();
        }
    }
}
