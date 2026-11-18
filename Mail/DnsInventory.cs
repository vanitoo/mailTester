using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;

namespace mailTester.Mail
{
    public sealed partial class DnsDiagnosticEngine
    {
        private async Task<IReadOnlyList<CheckFinding>> InventoryExtrasAsync(string domain, CancellationToken ct)
        {
            var checks = new (string Name, QueryType Type)[] {
                ("www." + domain, QueryType.A), ("www." + domain, QueryType.AAAA), ("www." + domain, QueryType.CNAME),
                ("mail." + domain, QueryType.A), ("mail." + domain, QueryType.AAAA),
                ("autodiscover." + domain, QueryType.CNAME), ("autoconfig." + domain, QueryType.CNAME),
                ("_autodiscover._tcp." + domain, QueryType.SRV), ("_submission._tcp." + domain, QueryType.SRV),
                ("_imaps._tcp." + domain, QueryType.SRV), ("_pop3s._tcp." + domain, QueryType.SRV),
                ("_mta-sts." + domain, QueryType.TXT), ("_smtp._tls." + domain, QueryType.TXT),
                ("default._bimi." + domain, QueryType.TXT)
            };
            var answers = await Task.WhenAll(checks.Select(x => Query(x.Name, x.Type, ct))).ConfigureAwait(false);
            var rows = answers.Select(x => Raw("DNS", x)).ToList();
            rows.Add(new("DNS", domain, CheckLevel.Info, "Обзор известных имён и типов", "DNS не предоставляет универсального списка всех поддоменов. ANY/AXFR и перебор произвольных имён не выполняются; отсутствующие записи не обязательно являются ошибками. DNSSEC-записи не означают проверенную DNSSEC-цепочку."));
            return rows;
        }
    }
}
