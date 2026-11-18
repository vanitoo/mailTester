using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace mailTester.Mail
{
    public static class RegistrationDiagnostics
    {
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        private static async Task<byte[]> ReadBoundedAsync(Stream stream, int limit, CancellationToken ct)
        {
            using var result = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                int n = await stream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (n == 0) break;
                if (result.Length + n > limit) throw new IOException("Ответ слишком большой.");
                result.Write(buffer, 0, n);
            }
            return result.ToArray();
        }
        private static async Task<JsonDocument> JsonAsync(Uri uri, CancellationToken ct)
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return JsonDocument.Parse(await ReadBoundedAsync(stream, 4 * 1024 * 1024, ct).ConfigureAwait(false));
        }

        private static string Text(JsonElement x, string key) =>
            x.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : "";
        private static DateTimeOffset? ParseDate(string text) =>
            DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

        private static CheckFinding Result(string section, string domain, DateTimeOffset? expiry, string detail)
        {
            if (expiry == null) return new(section, domain, CheckLevel.Warning, "Дата окончания не опубликована/не распознана", detail);
            double days = (expiry.Value - DateTimeOffset.UtcNow).TotalDays;
            return new(section, domain, days <= 0 ? CheckLevel.Error : days <= 30 ? CheckLevel.Warning : CheckLevel.Ok,
                $"Срок: {expiry.Value:yyyy-MM-dd} ({Math.Floor(days)} дн.)", detail + "\nДата регистрации/оплаты может отличаться у реестра и регистратора.");
        }

        private static async Task<string> WhoisAsync(string server, string query, CancellationToken ct)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(server, 43, ct).ConfigureAwait(false);
            using var stream = tcp.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(query + "\r\n").AsMemory(), ct).ConfigureAwait(false);
            return Encoding.UTF8.GetString(await ReadBoundedAsync(stream, 256 * 1024, ct).ConfigureAwait(false));
        }

        public static async Task<IReadOnlyList<CheckFinding>> InspectAsync(string domain, IProgress<string> progress, CancellationToken ct)
        {
            domain = DnsPolicyChecks.NormalizeDomain(domain);
            if (!domain.Contains('.')) throw new ArgumentException("Укажите зарегистрированное доменное имя.");
            string tld = domain.Split('.').Last();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(70));
            var token = timeout.Token;
            string rdapFailure = "Нет RDAP в IANA bootstrap.";
            progress?.Report("RDAP: поиск сервера IANA");
            try
            {
                using var bootstrap = await JsonAsync(new Uri("https://data.iana.org/rdap/dns.json"), token).ConfigureAwait(false);
                Uri endpoint = null;
                int best = -1;
                foreach (var service in bootstrap.RootElement.GetProperty("services").EnumerateArray())
                    foreach (var suffix in service[0].EnumerateArray())
                    {
                        string s = suffix.GetString()?.TrimEnd('.').ToLowerInvariant();
                        if (string.IsNullOrEmpty(s) || s.Length <= best || !(domain == s || domain.EndsWith("." + s, StringComparison.Ordinal))) continue;
                        foreach (var url in service[1].EnumerateArray())
                            if (Uri.TryCreate(url.GetString(), UriKind.Absolute, out var u) && u.Scheme == "https")
                            { endpoint = new Uri(u.AbsoluteUri.TrimEnd('/') + "/domain/" + domain); best = s.Length; break; }
                    }
                if (endpoint != null)
                {
                    progress?.Report("RDAP: запрос сведений о домене");
                    using var doc = await JsonAsync(endpoint, token).ConfigureAwait(false);
                    var root = doc.RootElement;
                    string foundName = Text(root, "ldhName");
                    if (!foundName.Equals(domain, StringComparison.OrdinalIgnoreCase)) throw new IOException("RDAP вернул другое имя.");
                    var lines = new List<string> { $"Источник: {endpoint}", $"Домен: {foundName}" };
                    DateTimeOffset? expiration = null;
                    if (root.TryGetProperty("events", out var events))
                        foreach (var ev in events.EnumerateArray())
                        {
                            string action = Text(ev, "eventAction"), date = Text(ev, "eventDate");
                            lines.Add(action + ": " + date);
                            if (action == "expiration" && ParseDate(date) is DateTimeOffset time && (expiration == null || time < expiration.Value))
                                expiration = time;
                        }
                    if (root.TryGetProperty("status", out var status)) lines.Add("Статусы: " + string.Join(", ", status.EnumerateArray().Select(x => x.GetString())));
                    if (root.TryGetProperty("nameservers", out var servers)) lines.Add("NS: " + string.Join(", ", servers.EnumerateArray().Select(x => Text(x, "ldhName"))));
                    if (root.TryGetProperty("entities", out var entities))
                        foreach (var entity in entities.EnumerateArray())
                            if (entity.TryGetProperty("roles", out var roles) && roles.EnumerateArray().Any(x => x.GetString() == "registrar"))
                            {
                                lines.Add("Регистратор (handle): " + Text(entity, "handle"));
                                if (entity.TryGetProperty("vcardArray", out var card) && card.GetArrayLength() > 1)
                                    foreach (var property in card[1].EnumerateArray())
                                        if (property.GetArrayLength() > 3 && property[0].GetString() == "fn" && property[3].ValueKind == JsonValueKind.String)
                                            lines.Add("Регистратор: " + property[3].GetString());
                            }
                    return new[] { Result("RDAP", domain, expiration, string.Join("\n", lines)) };
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { rdapFailure = "RDAP: " + ex.GetType().Name; }
            progress?.Report("WHOIS: резервный запрос (TCP 43)");
            try
            {
                string server;
                if (tld is "ru" or "su" or "xn--p1ai") server = "whois.tcinet.ru";
                else
                {
                    string iana = await WhoisAsync("whois.iana.org", tld, token).ConfigureAwait(false);
                    var match = Regex.Match(iana, @"^whois:\s*([^\s]+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (!match.Success) throw new IOException("Сервер WHOIS не опубликован.");
                    server = DnsPolicyChecks.NormalizeDomain(match.Groups[1].Value);
                }
                string raw = await WhoisAsync(server, domain, token).ConfigureAwait(false);
                var fields = raw.Split('\n').Select(x => x.TrimEnd('\r')).Where(x =>
                    Regex.IsMatch(x, @"^(?:domain(?: name)?|registrar|registrar name|creation date|created|registered|updated date|last updated|registry expiry date|registrar registration expiration date|expiration date|expiry date|expires|expiry|paid-till|free-date|status|domain status|state|nserver|name server)\s*:", RegexOptions.IgnoreCase)).ToArray();
                bool matchName = fields.Any(x => Regex.IsMatch(x, @"^domain(?: name)?\s*:", RegexOptions.IgnoreCase) && x[(x.IndexOf(':') + 1)..].Trim().Equals(domain, StringComparison.OrdinalIgnoreCase));
                if (!matchName) return new[] { new CheckFinding("WHOIS", domain, CheckLevel.Warning, "Домен/ответ не подтверждён", $"{rdapFailure}\nСервер: {server}\nНужен точный зарегистрированный домен; возможны ограничения WHOIS.") };
                var dates = fields.Where(x => Regex.IsMatch(x, @"^(?:registry expiry date|registrar registration expiration date|expiration date|expiry date|expires|expiry|paid-till)\s*:", RegexOptions.IgnoreCase))
                    .Select(x => ParseDate(x[(x.IndexOf(':') + 1)..].Trim())).Where(x => x.HasValue).OrderBy(x => x.Value).ToArray();
                return new[] { Result("WHOIS", domain, dates.FirstOrDefault(), $"{rdapFailure}\nСервер: {server} (TCP 43, без шифрования)\n" + string.Join("\n", fields)) };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return new[] { new CheckFinding("WHOIS", domain, CheckLevel.Warning, "Регистрационные сведения недоступны", rdapFailure + "\nWHOIS: " + ex.GetType().Name) }; }
        }
    }
}
