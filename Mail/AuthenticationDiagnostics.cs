using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;
using MimeKit;
using MimeKit.Cryptography;
using Org.BouncyCastle.Crypto;
using ARSoft.Tools.Net;
using ARSoft.Tools.Net.Spf;
using DnsStubResolver = ARSoft.Tools.Net.Dns.DnsStubResolver;

namespace mailTester.Mail
{
    public sealed partial class DnsDiagnosticEngine
    {
        // Explicit SMTP inputs; EML headers alone cannot authenticate the connecting IP.
        private record SpfResult(string Domain, string Result, CheckFinding Finding);
        private async Task<SpfResult> EvaluateSpfAsync(IPAddress ip, string mailFrom, string helo, CancellationToken ct)
        {
            if (ip == null) return new(null, "Unknown", new("SPF", "", CheckLevel.Skipped, "Нужен IP SMTP-клиента", "Введите IP, наблюдавшийся на границе доверенного MTA."));
            string sender = (mailFrom ?? "").Trim();
            helo = string.IsNullOrWhiteSpace(helo) ? null : DnsPolicyChecks.NormalizeDomain(helo);
            if (sender.Length == 0) return new(null, "Unknown", new("SPF", "", CheckLevel.Skipped, "Нужен SMTP MAIL FROM", "Введите envelope sender; для пустого reverse-path явно укажите <> и HELO."));
            string domain;
            if (sender == "<>")
            {
                if (helo == null) throw new ArgumentException("Для MAIL FROM <> необходим HELO.");
                domain = helo;
                sender = "postmaster@" + domain;
            }
            else
            {
                sender = sender.Trim('<', '>');
                int at = sender.LastIndexOf('@');
                if (at <= 0 || at == sender.Length - 1 || sender.Contains('\n') || sender.Contains('\r'))
                    throw new ArgumentException("MAIL FROM должен быть адресом или <>.");
                domain = DnsPolicyChecks.NormalizeDomain(sender[(at + 1)..]);
            }
            try
            {
                using var resolver = serverAddress == null ? new DnsStubResolver() : new DnsStubResolver(new[] { serverAddress }, 4000);
                var validator = new SpfValidator { DnsResolver = resolver, DnsLookupLimit = 10 };
                if (helo != null) validator.HeloDomain = DomainName.Parse(helo);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var result = await validator.CheckHostAsync(ip, domain, sender, false, timeout.Token).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                string code = result.Result.ToString();
                var level = code.ToLowerInvariant() switch { "pass" => CheckLevel.Ok, "fail" or "permerror" or "temperror" => CheckLevel.Error, _ => CheckLevel.Warning };
                return new(domain, code, new("SPF", domain, level, code.ToUpperInvariant(),
                    $"SMTP IP: {ip}\nMAIL FROM identity: {sender}\nHELO: {helo ?? "(не указан)"}\nРекурсивное вычисление через ARSoft.Tools.Net (лимит 10 в валидаторе). Результат относится к введённым данным и текущему DNS."));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return new(domain, "Unknown", new("SPF", domain, CheckLevel.Error, "Не удалось вычислить SPF", ex.GetType().Name)); }
        }

        public async Task<IReadOnlyList<CheckFinding>> CheckSpfAsync(IPAddress ip, string mailFrom, string helo, CancellationToken ct)
        {
            var answer = await EvaluateSpfAsync(ip, mailFrom, helo, ct).ConfigureAwait(false);
            return new[] { answer.Finding };
        }

        public record DmarcDiscovery(string Domain, string Organization, string Source, Dictionary<string,string> Tags,
            string Record, string Policy, bool Unknown, string Trace);

        public static string[] TreeWalkNames(string domain)
        {
            domain = DnsPolicyChecks.NormalizeDomain(domain);
            var labels = domain.Split('.');
            var names = new List<string> { domain };
            for (int remaining = Math.Min(labels.Length - 1, 7); remaining >= 1; remaining--)
                names.Add(string.Join(".", labels.Skip(labels.Length - remaining)));
            return names.ToArray();
        }

        public async Task<DmarcDiscovery> DiscoverDmarcAsync(string domain, CancellationToken ct)
        {
            domain = DnsPolicyChecks.NormalizeDomain(domain);
            var valid = new List<(string Name, string Record, Dictionary<string,string> Tags)>();
            var trace = new List<string>();
            bool unknown = false;
            foreach (string name in TreeWalkNames(domain))
            {
                var answer = await Query("_dmarc." + name, QueryType.TXT, ct).ConfigureAwait(false);
                if (answer.Failed) { trace.Add(name + ": DNS error " + answer.Text); unknown = true; break; }
                var records = answer.Txt.Where(x => Regex.IsMatch(x, @"^v\s*=\s*DMARC1\s*(?:;|$)")).ToArray();
                if (records.Length != 1 || !DnsPolicyChecks.Tags(records[0], out var tags))
                {
                    trace.Add(name + (records.Length > 1 ? ": duplicate/discarded" : ": no valid record"));
                    continue;
                }
                valid.Add((name, records[0], tags));
                trace.Add(name + ": " + records[0]);
                if (tags.TryGetValue("psd", out string psd) && (psd == "n" || psd == "y")) break;
            }
            string org = domain;
            if (valid.Count > 0)
            {
                var highest = valid[^1];
                if (highest.Tags.TryGetValue("psd", out string psd) && psd == "y")
                {
                    int count = highest.Name.Split('.').Length + 1;
                    var labels = domain.Split('.');
                    org = labels.Length >= count ? string.Join(".", labels.Skip(labels.Length - count)) : domain;
                }
                else org = highest.Name;
            }
            var chosen = valid.FirstOrDefault(x => x.Name == domain);
            if (chosen.Tags == null) chosen = valid.FirstOrDefault(x => x.Name == org);
            if (chosen.Tags == null && valid.Count > 0 && valid[^1].Tags.TryGetValue("psd", out string flag) && flag == "y") chosen = valid[^1];
            if (chosen.Tags == null)
                return new(domain, org, null, null, null, null, unknown, string.Join("\n", trace));
            var t = chosen.Tags;
            bool ValidPolicy(string v) => v is "none" or "quarantine" or "reject";
            bool validPolicy = t.TryGetValue("p", out string policy) && ValidPolicy(policy);
            foreach (var key in new[] { "sp", "np" })
                if (t.TryGetValue(key, out var v) && !ValidPolicy(v)) validPolicy = false;
            if (!validPolicy)
            {
                bool rua = t.TryGetValue("rua", out var reports) && reports.Split(',').Any(x => Uri.TryCreate(x.Trim(), UriKind.Absolute, out var u) && u.Scheme == "mailto" && u.OriginalString.Contains('@'));
                policy = rua ? "none" : null;
                trace.Add("Некорректная политика: " + (rua ? "p=none (fallback по rua)" : "DMARC не применяется"));
            }
            else if (chosen.Name != domain)
            {
                var exists = await Query(domain, QueryType.A, ct).ConfigureAwait(false);
                if (exists.Failed) { unknown = true; trace.Add("Не удалось определить существование авторского домена."); }
                else
                {
                    string tag = exists.Code == 3 ? "np" : "sp";
                    if (t.TryGetValue(tag, out var childPolicy)) policy = childPolicy;
                    else if (exists.Code == 3 && t.TryGetValue("sp", out var subPolicy)) policy = subPolicy;
                }
            }
            return new(domain, org, chosen.Name, t, chosen.Record, policy, unknown, string.Join("\n", trace));
        }

        internal static CheckFinding DmarcFinding(DmarcDiscovery d)
        {
            string detail = $"Авторский домен: {d.Domain}\nОрганизационный домен: {d.Organization}\nИсточник: {d.Source ?? "(нет)"}\nЭффективная политика: {d.Policy ?? "(нет)"}\n\nDNS Tree Walk (RFC 9989):\n{d.Trace}";
            if (d.Unknown) return new("DMARC", d.Domain, CheckLevel.Unknown, "Неполный DNS Tree Walk", detail);
            if (d.Policy == null) return new("DMARC", d.Domain, CheckLevel.Warning, "Применимая политика не обнаружена", detail);
            bool test = d.Tags.TryGetValue("t", out string flag) && flag == "y";
            if (test) detail += "\n\nRFC 9989 t=y: ожидаемая политика для FAIL на один уровень мягче: " + ExpectedTestPolicy(d.Policy);
            if (d.Tags.ContainsKey("pct")) detail += "\npct: historic в RFC 9989; не полагайтесь на процентную выборку.";
            if (!d.Tags.ContainsKey("rua")) detail += "\nrua отсутствует: агрегированные отчёты не запрошены.";
            return new("DMARC", d.Domain, d.Policy == "none" || test || d.Tags.ContainsKey("pct") ? CheckLevel.Warning : CheckLevel.Ok,
                $"p={d.Policy}" + (d.Source == d.Domain ? " (прямая)" : " (наследуется)") + (test ? "; t=y" : ""), detail);
        }

        public static string ExpectedTestPolicy(string policy) => policy switch { "reject" => "quarantine", "quarantine" => "none", _ => "none" };

        private sealed class KeyLocator : DkimPublicKeyLocatorBase
        {
            private readonly DnsDiagnosticEngine engine;
            public bool DnsFailed { get; set; }
            public KeyLocator(DnsDiagnosticEngine engine) { this.engine = engine; }

            public override AsymmetricKeyParameter LocatePublicKey(string methods, string domain, string selector, CancellationToken cancellationToken = default) =>
                LocatePublicKeyAsync(methods, domain, selector, cancellationToken).GetAwaiter().GetResult();

            public override async Task<AsymmetricKeyParameter> LocatePublicKeyAsync(string methods, string domain, string selector, CancellationToken cancellationToken = default)
            {
                if (!methods.Split(':').Contains("dns/txt", StringComparer.OrdinalIgnoreCase)) throw new NotSupportedException("DKIM lookup method");
                var d = DnsPolicyChecks.NormalizeDomain(domain);
                var s = DnsPolicyChecks.ParseSelectors(selector);
                if (s.Length != 1 || s[0].Length + d.Length + 12 > 253) throw new FormatException("DKIM selector");
                var answer = await engine.Query(s[0] + "._domainkey." + d, QueryType.TXT, cancellationToken).ConfigureAwait(false);
                if (answer.Failed) { DnsFailed = true; throw new IOException("DKIM DNS query failed"); }
                var keys = answer.Txt.Where(x => Regex.IsMatch(x, @"(?:^|;)\s*p\s*=", RegexOptions.IgnoreCase)).ToArray();
                if (keys.Length != 1) throw new FormatException("No unique key");
                return GetPublicKey(keys[0]);
            }
        }

        public async Task<IReadOnlyList<CheckFinding>> AnalyzeEmlAsync(string file, IPAddress ip, string mailFrom, string helo, IProgress<string> progress, CancellationToken ct)
        {
            if (new FileInfo(file).Length > 50 * 1024 * 1024) throw new ArgumentException("EML превышает 50 МБ.");
            using var message = await MimeMessage.LoadAsync(file, ct).ConfigureAwait(false);
            var rows = new List<CheckFinding>();
            rows.Add(new("EML", "Overview", CheckLevel.Info, message.Subject ?? "(без темы)",
                $"From: {message.From}\nTo: {message.To}\nDate: {message.Date:O}\nMessage-ID: {message.MessageId}\nAttachments: {message.Attachments.Count()}"));
            rows.AddRange(MailHeaderDiagnostics.AnalyzeRoute(message.Headers.Where(h => h.Field.Equals("Received", StringComparison.OrdinalIgnoreCase)).Select(h => h.Value)));
            foreach (var h in message.Headers.Where(h => h.Field.Equals("Received", StringComparison.OrdinalIgnoreCase) ||
                                                       h.Field.Equals("Authentication-Results", StringComparison.OrdinalIgnoreCase) ||
                                                       h.Field.Equals("Received-SPF", StringComparison.OrdinalIgnoreCase) ||
                                                       h.Field.Equals("Return-Path", StringComparison.OrdinalIgnoreCase)).Take(60))
                rows.Add(new("EML", h.Field, CheckLevel.Info, "Заголовок: не доверенный сам по себе", h.Value));

            progress?.Report("SPF: проверка введённых SMTP-данных");
            SpfResult spf = await EvaluateSpfAsync(ip, mailFrom, helo, ct).ConfigureAwait(false);
            rows.Add(spf.Finding);
            progress?.Report("DKIM: криптографическая проверка исходного письма");
            var locator = new KeyLocator(this);
            var verifier = new DkimVerifier(locator);
            var signedDomains = new List<string>();
            bool unknownSignature = false;
            var signatures = message.Headers.Where(h => h.Id == HeaderId.DkimSignature).ToArray();
            if (signatures.Length == 0) rows.Add(new("DKIM", "EML", CheckLevel.Warning, "DKIM-Signature отсутствует", "Подпись не обнаружена."));
            foreach (var header in signatures.Take(10))
            {
                ct.ThrowIfCancellationRequested();
                DnsPolicyChecks.Tags(header.Value, out var tags);
                tags.TryGetValue("d", out string signing);
                tags.TryGetValue("s", out string selector);
                string name = (selector ?? "?") + "._domainkey." + (signing ?? "?");
                try
                {
                    locator.DnsFailed = false;
                    bool verified = await verifier.VerifyAsync(message, header, ct).ConfigureAwait(false);
                    if (locator.DnsFailed) { unknownSignature = true; rows.Add(new("DKIM", name, CheckLevel.Warning, "DNS lookup incomplete", "Проверка подписи не завершена.")); continue; }
                    if (verified && signing != null) signedDomains.Add(DnsPolicyChecks.NormalizeDomain(signing));
                    bool partialBody = tags.ContainsKey("l");
                    rows.Add(new("DKIM", name, verified ? partialBody ? CheckLevel.Warning : CheckLevel.Ok : CheckLevel.Error, verified ? "PASS: подпись проверена" + (partialBody ? " (l=)" : "") : "FAIL: подпись не подтверждена", "MimeKit проверяет подписанные данные по текущему ключу DNS. Изменённый EML или ротация ключей могут изменить результат." + (partialBody ? " Тег l= допускает неподписанный хвост тела; не считайте всё тело аутентифицированным." : "")));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { unknownSignature = true; rows.Add(new("DKIM", name, CheckLevel.Warning, "Не удалось проверить подпись", ex.GetType().Name)); }
            }
            if (signatures.Length > 10) { unknownSignature = true; rows.Add(new("DKIM", "EML", CheckLevel.Skipped, "Лимит 10 подписей", "Остальные подписи не проверялись.")); }
            var authorDomains = message.From.Mailboxes.Select(x => x.Address.Split('@').Last()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (authorDomains.Length != 1) { rows.Add(new("DMARC", "EML", CheckLevel.Skipped, "Нужен единственный авторский домен", "")); return rows; }
            string author = DnsPolicyChecks.NormalizeDomain(authorDomains[0]);
            if (message.Headers.Count(x => x.Id == HeaderId.From) != 1)
            {
                rows.Add(new("DMARC", author, CheckLevel.Skipped, "Неоднозначный From", "Нужен ровно один заголовок From."));
                return rows;
            }
            progress?.Report("DMARC: поиск политики и alignment");
            var policy = await DiscoverDmarcAsync(author, ct).ConfigureAwait(false);
            rows.Add(DmarcFinding(policy));
            if (policy.Policy == null || policy.Unknown)
            {
                rows.Add(new("DMARC", author, CheckLevel.Skipped, "Результат DMARC не определён", "Не найдена применимая политика или DNS Tree Walk не завершён."));
                return rows;
            }
            bool uncertain = unknownSignature || spf.Result.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                spf.Result.Equals("TempError", StringComparison.OrdinalIgnoreCase);
            async Task<bool> Aligned(string identity, string tag)
            {
                if (identity == author) return true;
                if (policy.Tags.TryGetValue(tag, out string mode) && mode == "s") return false;
                var candidate = await DiscoverDmarcAsync(identity, ct).ConfigureAwait(false);
                if (candidate.Unknown) { uncertain = true; return false; }
                return candidate.Organization == policy.Organization;
            }
            bool pass = false;
            var alignments = new List<string>();
            if (spf.Result.Equals("Pass", StringComparison.OrdinalIgnoreCase) && spf.Domain != null)
            {
                bool a = await Aligned(spf.Domain, "aspf").ConfigureAwait(false);
                pass |= a; alignments.Add($"SPF {spf.Domain}: aligned={a}");
            }
            foreach (var d in signedDomains.Distinct())
            {
                bool a = await Aligned(d, "adkim").ConfigureAwait(false);
                pass |= a; alignments.Add($"DKIM {d}: aligned={a}");
            }
            rows.Add(new("DMARC", author, pass ? CheckLevel.Ok : uncertain ? CheckLevel.Warning : CheckLevel.Error,
                pass ? "PASS" : uncertain ? "INDETERMINATE" : "FAIL",
                $"Политика: {policy.Policy}\n" + string.Join("\n", alignments) + "\nПроверка по текущему DNS и явно введённым SMTP-данным; заголовки Authentication-Results не используются как доказательство. ARC и локальная политика принимающего MTA не оцениваются."));
            return rows;
        }
    }
}
