using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace mailTester.Mail
{
    public enum CheckLevel { Info, Ok, Warning, Error, Skipped }
    public record CheckFinding(string Section, string Name, CheckLevel Level, string Summary, string Detail);

    public static class DnsPolicyChecks
    {
        public static string NormalizeDomain(string input)
        {
            string value = (input ?? "").Trim().TrimEnd('.');
            if (value.Length == 0 || IPAddress.TryParse(value, out _))
                throw new ArgumentException("Введите DNS-имя домена, а не IP, URL или адрес почты.");
            string ascii;
            try { ascii = new IdnMapping { UseStd3AsciiRules = true }.GetAscii(value).ToLowerInvariant(); }
            catch (ArgumentException) { throw new ArgumentException("Некорректное DNS-имя."); }
            if (ascii.Length > 253 || ascii.Split('.').Any(label => !Regex.IsMatch(label, @"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")))
                throw new ArgumentException("Некорректное DNS-имя.");
            return ascii;
        }

        public static string[] ParseSelectors(string input)
        {
            var values = Regex.Split((input ?? "").Trim(), @"[\s,;]+")
                .Where(x => x.Length > 0).Select(x => x.ToLowerInvariant()).Distinct().ToArray();
            if (values.Length > 10) throw new ArgumentException("Не более 10 DKIM-селекторов.");
            foreach (var value in values)
                if (value.Length > 180 || value.Split('.').Any(x => !Regex.IsMatch(x, @"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")))
                    throw new ArgumentException("Некорректный DKIM-селектор.");
            return values;
        }

        private static bool SpfVersion(string text) =>
            Regex.IsMatch(text, @"^v=spf1(?: |$)", RegexOptions.IgnoreCase);

        public static CheckFinding Spf(string domain, IReadOnlyList<string> txt)
        {
            var records = txt.Where(SpfVersion).ToArray();
            if (records.Length == 0) return new("SPF", domain, CheckLevel.Warning, "SPF не опубликован", "На указанном имени нет TXT v=spf1.");
            if (records.Length != 1) return new("SPF", domain, CheckLevel.Error, "Несколько SPF-записей (PermError)", string.Join("\n", records));
            string record = records[0];
            var terms = record.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
            var issues = new List<string>();
            var notes = new List<string>();
            int lookupTerms = 0;
            string all = null;
            string redirect = null;
            bool unreachable = false;
            foreach (string raw in terms)
            {
                string term = raw.ToLowerInvariant();
                if (term.Contains('='))
                {
                    if (term.StartsWith("redirect="))
                    {
                        if (redirect != null || term.Length == 9) issues.Add("Пустой или повторный redirect.");
                        redirect = term[9..];
                    }
                    continue; // unknown modifiers are ignored by SPF
                }
                char qualifier = term.Length > 0 && "+-~?".Contains(term[0]) ? term[0] : '+';
                if (term.Length > 0 && "+-~?".Contains(term[0])) term = term[1..];
                string mechanism = term.Split(':', '/')[0];
                if (!new[] { "all", "include", "a", "mx", "ptr", "ip4", "ip6", "exists" }.Contains(mechanism))
                    issues.Add("Неизвестный механизм: " + raw);
                if (mechanism == "all" && term != "all") issues.Add("Некорректный all.");
                if ((mechanism == "include" || mechanism == "exists") && (!term.StartsWith(mechanism + ":") || term.Length == mechanism.Length + 1))
                    issues.Add("Отсутствует domain-spec: " + raw);
                if (mechanism == "ip4" || mechanism == "ip6")
                {
                    var parts = term.StartsWith(mechanism + ":") ? term[(mechanism.Length + 1)..].Split('/') : Array.Empty<string>();
                    bool ipv4 = mechanism == "ip4";
                    if (parts.Length < 1 || parts.Length > 2 || !IPAddress.TryParse(parts[0], out var address) ||
                        (ipv4 ? address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork : address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) ||
                        (parts.Length == 2 && (!int.TryParse(parts[1], out int bits) || bits < 0 || bits > (ipv4 ? 32 : 128))))
                        issues.Add("Некорректный IP/CIDR: " + raw);
                }
                if (all != null) { unreachable = true; continue; }
                if (new[] { "include", "a", "mx", "ptr", "exists" }.Contains(mechanism)) lookupTerms++;
                if (mechanism == "ptr") notes.Add("Механизм ptr не рекомендуется.");
                if (mechanism == "all" && term == "all") all = qualifier + "all";
            }
            if (redirect != null && all == null) lookupTerms++;
            if (unreachable) notes.Add("Механизмы после all недостижимы.");
            if (all == "+all") notes.Add("Любой отправитель разрешён (+all).");
            if (all == "?all") notes.Add("Нейтральная политика (?all).");
            if (all == null && redirect == null) notes.Add("Нет явного all или redirect.");
            if (redirect != null && all != null) notes.Add("redirect игнорируется при наличии all.");
            if (lookupTerms > 10) notes.Add("Возможное превышение лимита 10 DNS-термов; фактическое число зависит от пути вычисления.");
            notes.Add($"DNS-термы верхнего уровня: {lookupTerms}. Вложенные include/redirect и фактический лимит не вычисляются.");
            notes.Add("Это статическая проверка, не SPF PASS/FAIL для конкретного IP.");
            var level = issues.Count > 0 ? CheckLevel.Error : notes.Count > 2 ? CheckLevel.Warning : CheckLevel.Ok;
            return new("SPF", domain, level, issues.Count > 0 ? "Обнаружены ошибки записи" : "SPF: " + (all ?? (redirect != null ? "redirect" : "без all")),
                record + "\n\n" + string.Join("\n", issues.Concat(notes)));
        }

        private static bool Tags(string text, out Dictionary<string,string> tags)
        {
            tags = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in text.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(part)) continue;
                int eq = part.IndexOf('=');
                if (eq < 1 || !tags.TryAdd(part[..eq].Trim(), part[(eq + 1)..].Trim())) return false;
            }
            return tags.Count > 0;
        }

        public static CheckFinding Dmarc(string name, IReadOnlyList<string> txt)
        {
            var records = txt.Where(x => Regex.IsMatch(x, @"^v\s*=\s*DMARC1\s*(?:;|$)", RegexOptions.IgnoreCase)).ToArray();
            if (records.Length == 0) return new("DMARC", name, CheckLevel.Warning, "Нет прямой DMARC-записи", "Политика может наследоваться от вышестоящего домена; поиск по DNS-дереву не выполняется.");
            if (records.Length != 1) return new("DMARC", name, CheckLevel.Error, "Несколько DMARC-записей", string.Join("\n", records));
            string record = records[0];
            if (!Tags(record, out var tags))
                return new("DMARC", name, CheckLevel.Error, "Некорректные или повторные теги", record);
            var issues = new List<string>();
            foreach (string tag in new[] { "p", "sp", "np" })
                if ((tag == "p" || tags.ContainsKey(tag)) && (!tags.TryGetValue(tag, out var value) || !new[] { "none", "quarantine", "reject" }.Contains(value)))
                    issues.Add("Отсутствует или некорректна политика " + tag + ".");
            foreach (string tag in new[] { "adkim", "aspf" })
                if (tags.TryGetValue(tag, out var value) && value != "r" && value != "s") issues.Add("Недопустимый режим " + tag + ".");
            if (tags.TryGetValue("t", out var testing) && testing != "y" && testing != "n") issues.Add("Недопустимое значение t.");
            if (issues.Count > 0) return new("DMARC", name, CheckLevel.Error, "Проблемы с тегами политики", record + "\n" + string.Join("\n", issues));
            string policy = tags["p"];
            bool test = testing == "y";
            var notes = new List<string> { "Запрошенная политика p=" + policy + (test ? "; t=y (тестирование)." : ".") };
            notes.Add(tags.ContainsKey("rua") ? "rua указан; адреса и право внешнего получателя на отчёты не проверены." : "rua не указан; агрегированные отчёты не запрошены.");
            if (tags.ContainsKey("pct")) notes.Add("pct — устаревший тег RFC 7489; в RFC 9989 он имеет статус historic.");
            notes.Add("Запись не подтверждает прохождение DMARC конкретным письмом.");
            return new("DMARC", name, policy == "none" || test ? CheckLevel.Warning : CheckLevel.Ok, "Политика: " + policy + (test ? " (тест)" : ""), record + "\n\n" + string.Join("\n", notes));
        }

        public static CheckFinding Dkim(string name, IReadOnlyList<string> txt)
        {
            var records = txt.Where(x => Regex.IsMatch(x, @"(?:^|;)\s*(?:p\s*=|v\s*=\s*DKIM1(?:\s*;|\s*$))", RegexOptions.IgnoreCase)).ToArray();
            if (records.Length == 0) return new("DKIM", name, CheckLevel.Warning, "Ключ не найден", "Проверьте селектор из заголовка DKIM-Signature (s=), а также подписывающий домен d=.");
            if (records.Length != 1) return new("DKIM", name, CheckLevel.Error, "Несколько записей ключа", string.Join("\n", records));
            string record = records[0];
            if (!Tags(record, out var tags) || (tags.TryGetValue("v", out var version) && version != "DKIM1") || !tags.TryGetValue("p", out var key))
                return new("DKIM", name, CheckLevel.Error, "Некорректные теги ключа", record);
            if (key.Length == 0) return new("DKIM", name, CheckLevel.Warning, "Ключ отозван (p= пуст)", record);
            string algorithm = tags.TryGetValue("k", out var type) ? type.ToLowerInvariant() : "rsa";
            try
            {
                byte[] bytes = Convert.FromBase64String(key);
                if (algorithm == "ed25519")
                    return new("DKIM", name, bytes.Length == 32 ? CheckLevel.Ok : CheckLevel.Error, bytes.Length == 32 ? "Ed25519: ключ опубликован" : "Некорректная длина Ed25519", record + "\nПодпись письма не проверялась.");
                if (algorithm != "rsa") return new("DKIM", name, CheckLevel.Warning, "Неизвестный алгоритм: " + algorithm, record);
                using var rsa = RSA.Create();
                int read;
                try { rsa.ImportSubjectPublicKeyInfo(bytes, out read); }
                catch (CryptographicException) { rsa.ImportRSAPublicKey(bytes, out read); }
                if (read != bytes.Length) throw new CryptographicException();
                int bits = rsa.KeySize;
                return new("DKIM", name, bits < 1024 ? CheckLevel.Error : bits < 2048 ? CheckLevel.Warning : CheckLevel.Ok,
                    "RSA " + bits + " бит" + (bits < 2048 ? " (рекомендуется 2048+)" : ""), record + "\nПодпись конкретного письма не проверялась.");
            }
            catch (Exception ex) when (ex is FormatException || ex is CryptographicException || ex is ArgumentException)
            { return new("DKIM", name, CheckLevel.Error, "Некорректный публичный ключ", record); }
        }
    }
}
