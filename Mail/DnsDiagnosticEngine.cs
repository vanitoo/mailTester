using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using DnsClient.Protocol;

namespace mailTester.Mail
{
    public record DnsDiagnosticReport(string Domain, string Resolver, DateTimeOffset Timestamp, IReadOnlyList<CheckFinding> Findings);

    public sealed partial class DnsDiagnosticEngine
    {
        private readonly LookupClient client;
        private readonly string resolver;
        private readonly IPAddress serverAddress;

        public DnsDiagnosticEngine(IPAddress server = null)
        {
            serverAddress = server;
            var options = server == null ? new LookupClientOptions() : new LookupClientOptions(server);
            options.UseCache = false;
            options.Timeout = TimeSpan.FromSeconds(4);
            options.Retries = 1;
            options.ThrowDnsErrors = false;
            options.UseTcpFallback = true;
            client = new LookupClient(options);
            resolver = server?.ToString() ?? "Системный DNS";
        }

        private record Answer(string Name, QueryType Type, int Code, IReadOnlyList<DnsResourceRecord> Records, string Error)
        {
            public bool Failed => Error != null || (Code != 0 && Code != 3);
            public bool Missing => Code == 3 || Records.Count == 0;
            public string Text => Error ?? (Code == 3 ? "NXDOMAIN" : Code != 0 ? "DNS RCODE " + Code : Records.Count == 0 ? "NOERROR / NODATA" : string.Join("\n", Records.Select(x => x.ToString())));
            public string[] Txt => Records.OfType<TxtRecord>().Select(x => string.Concat(x.Text)).ToArray();
        }

        private async Task<Answer> Query(string name, QueryType type, CancellationToken ct)
        {
            try
            {
                string current = name.TrimEnd('.') + ".";
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var aliases = new List<DnsResourceRecord>();
                for (int i = 0; i < 8; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!visited.Add(current)) return new(name, type, -1, aliases, "Цикл CNAME");
                    var response = await client.QueryAsync(current, type, cancellationToken: ct).ConfigureAwait(false);
                    int code = (int)response.Header.ResponseCode;
                    var records = response.Answers.ToArray();
                    if (code != 0) return new(name, type, code, aliases.Concat(records).ToArray(), null);
                    var matching = records.Where(x => (int)x.RecordType == (int)type).ToArray();
                    if (matching.Length > 0) return new(name, type, code, aliases.Concat(records).ToArray(), null);
                    var cname = records.OfType<CNameRecord>().LastOrDefault();
                    if (cname == null || type == QueryType.CNAME) return new(name, type, code, aliases.Concat(records).ToArray(), null);
                    aliases.AddRange(records);
                    current = cname.CanonicalName.Value.TrimEnd('.') + ".";
                }
                return new(name, type, -1, aliases, "Слишком длинная цепочка CNAME");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return new(name, type, -1, Array.Empty<DnsResourceRecord>(), ex.GetType().Name); }
        }

        private static CheckFinding Raw(string section, Answer answer)
        {
            int count = answer.Records.Count(x => (int)x.RecordType == (int)answer.Type);
            return new(section, answer.Name + " " + answer.Type, answer.Failed ? CheckLevel.Error : answer.Code == 3 || count == 0 ? CheckLevel.Info : CheckLevel.Ok,
                answer.Failed ? "Ошибка DNS" : answer.Code == 3 ? "NXDOMAIN" : count == 0 ? "Нет записи (NODATA)" : $"Записей: {count}", answer.Text);
        }

        private static CheckFinding FailedPolicy(string section, Answer answer) =>
            new(section, answer.Name, CheckLevel.Error, "Политику не удалось проверить: ошибка DNS", answer.Text);

        public async Task<DnsDiagnosticReport> InspectAsync(string domain, string[] selectors, IProgress<string> progress, CancellationToken ct)
        {
            domain = DnsPolicyChecks.NormalizeDomain(domain);
            selectors = DnsPolicyChecks.ParseSelectors(string.Join(" ", selectors ?? Array.Empty<string>()));
            if (selectors.Any(x => x.Length + domain.Length + 12 > 253) || domain.Length + 7 > 253)
                throw new ArgumentException("Слишком длинное имя для проверки политики.");
            var rows = new List<CheckFinding>();
            progress?.Report("Запросы A, AAAA, NS, SOA, TXT, MX");
            QueryType[] types = { QueryType.A, QueryType.AAAA, QueryType.NS, QueryType.SOA, QueryType.TXT, QueryType.CNAME, QueryType.SRV, (QueryType)257, (QueryType)43, (QueryType)48, QueryType.MX };
            var replies = await Task.WhenAll(types.Select(t => Query(domain, t, ct))).ConfigureAwait(false);
            foreach (var answer in replies) rows.Add(Raw(answer.Type == QueryType.MX ? "MX" : "DNS", answer));
            var mx = replies.Last();
            var hosts = mx.Records.OfType<MxRecord>().OrderBy(x => x.Preference).ToArray();
            if (!mx.Failed)
            {
                if (mx.Code == 3) rows.Add(new("MX", domain, CheckLevel.Error, "Домен не существует по ответу MX", "NXDOMAIN."));
                else if (hosts.Length == 0)
                {
                    var addressReplies = replies.Where(x => x.Type == QueryType.A || x.Type == QueryType.AAAA).ToArray();
                    bool hasAddress = addressReplies.Any(x => !x.Failed && x.Code == 0 && x.Records.Any(r => (int)r.RecordType == (int)x.Type));
                    bool incomplete = addressReplies.Any(x => x.Failed);
                    rows.Add(new("MX", domain, hasAddress || incomplete ? CheckLevel.Warning : CheckLevel.Error,
                        hasAddress ? "Явного MX нет: возможен implicit MX" : incomplete ? "Явного MX нет, fallback не проверен" : "Нет MX и адресов для implicit MX",
                        "SMTP-доступность не проверялась.\n" + string.Join("\n\n", addressReplies.Select(x => x.Type + ":\n" + x.Text))));
                }
                else if (hosts.Any(x => x.Exchange.Value.TrimEnd('.').Length == 0))
                {
                    bool valid = hosts.Length == 1 && hosts[0].Preference == 0;
                    rows.Add(new("MX", domain, valid ? CheckLevel.Info : CheckLevel.Error, valid ? "Null MX: домен не принимает почту" : "Некорректная комбинация Null MX", mx.Text));
                }
                else
                {
                    progress?.Report("Разрешение адресов MX");
                    var distinctHosts = hosts.Select(x => x.Exchange.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    foreach (string host in distinctHosts.Take(10))
                    {
                        var ip = await Task.WhenAll(Query(host, QueryType.A, ct), Query(host, QueryType.AAAA, ct)).ConfigureAwait(false);
                        bool found = ip.Any(x => !x.Failed && x.Code == 0 && x.Records.Any(r => (int)r.RecordType == (int)x.Type));
                        bool alias = ip.Any(x => x.Records.OfType<CNameRecord>().Any());
                        bool failed = ip.Any(x => x.Failed);
                        rows.Add(new("MX", host, alias || failed ? CheckLevel.Warning : found ? CheckLevel.Ok : CheckLevel.Error,
                            alias ? "MX указывает на CNAME (не рекомендуется/недопустимо для MX)" : failed ? "Часть DNS-запросов не завершилась" : found ? "MX разрешается в IP" : "Нет A/AAAA у MX",
                            string.Join("\n\n", ip.Select(x => x.Type + ":\n" + x.Text))));
                    }
                    if (distinctHosts.Length > 10) rows.Add(new("MX", domain, CheckLevel.Skipped, "Лимит проверки 10 MX-хостов", "Остальные хосты не проверялись."));
                }
            }
            progress?.Report("Анализ SPF и DMARC");
            var txt = replies.Single(x => x.Type == QueryType.TXT);
            rows.Add(txt.Failed ? FailedPolicy("SPF", txt) : DnsPolicyChecks.Spf(domain, txt.Txt));
            var dmarc = await DiscoverDmarcAsync(domain, ct).ConfigureAwait(false);
            rows.Add(DmarcFinding(dmarc));
            rows.AddRange(await InventoryExtrasAsync(domain, ct).ConfigureAwait(false));
            if (selectors.Length == 0) rows.Add(new("DKIM", domain, CheckLevel.Skipped, "Укажите DKIM-селектор", "Селектор s= и подписывающий домен d= можно взять из DKIM-Signature. Угадывание не выполняется."));
            else
            {
                progress?.Report("Проверка DKIM");
                var results = await Task.WhenAll(selectors.Select(x => Query(x + "._domainkey." + domain, QueryType.TXT, ct))).ConfigureAwait(false);
                foreach (var result in results)
                    rows.Add(result.Failed ? FailedPolicy("DKIM", result) : DnsPolicyChecks.Dkim(result.Name, result.Txt));
            }
            ct.ThrowIfCancellationRequested();
            return new(domain, resolver, DateTimeOffset.Now, rows);
        }
    }
}
