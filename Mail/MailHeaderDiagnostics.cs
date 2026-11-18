using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace mailTester.Mail
{
    public static class MailHeaderDiagnostics
    {
        public record RouteHop(string From, string By, DateTimeOffset? Date, double? DelaySeconds, string Raw);

        public static IReadOnlyList<RouteHop> ParseRoute(IEnumerable<string> received)
        {
            // Received headers normally arrive newest first. They may be forged or reordered.
            var result = new List<RouteHop>();
            DateTimeOffset? previous = null;
            foreach (string raw in received.Take(60).Reverse())
            {
                string value = raw ?? "";
                string from = Regex.Match(value, @"\bfrom\s+([^\s(;]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                string by = Regex.Match(value, @"\bby\s+([^\s(;]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                int semicolon = value.LastIndexOf(';');
                DateTimeOffset? date = null;
                if (semicolon >= 0)
                {
                    string tail = Regex.Replace(value[(semicolon + 1)..], @"\([^()]*\)", "").Trim();
                    // RFC 5322 numeric UTC offset; normalize +HHMM for .NET parsing.
                    tail = Regex.Replace(tail, @"([+-]\d{2})(\d{2})$", "$1:$2");
                    if (Regex.IsMatch(tail, @"(?:[+-]\d{2}:\d{2}|GMT|UT|UTC|Z)$", RegexOptions.IgnoreCase) &&
                        DateTimeOffset.TryParse(tail, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var time))
                        date = time;
                }
                double? delta = previous.HasValue && date.HasValue ? (date.Value - previous.Value).TotalSeconds : null;
                result.Add(new(from, by, date, delta, value));
                previous = date;
            }
            return result;
        }

        public static IReadOnlyList<CheckFinding> AnalyzeRoute(IEnumerable<string> received)
        {
            var route = ParseRoute(received);
            if (route.Count == 0) return new[] { new CheckFinding("EML", "Route", CheckLevel.Info, "Нет Received", "Маршрут не восстановить.") };
            var rows = new List<CheckFinding> {
                new("EML", "Route", CheckLevel.Info, "Предполагаемый маршрут (от старых к новым)", "Порядок по заголовкам, не по проверенным узлам. IP, имена и время могут быть подделаны. Отрицательные интервалы возможны при расхождении часов. До 60 строк.")
            };
            int i = 0;
            foreach (var hop in route)
            {
                string delay = hop.DelaySeconds.HasValue ? $" | Δ {hop.DelaySeconds.Value:F1} с" : "";
                rows.Add(new("EML", $"Hop {++i}", hop.DelaySeconds < 0 ? CheckLevel.Warning : CheckLevel.Info,
                    $"{(hop.From.Length == 0 ? "?" : hop.From)} → {(hop.By.Length == 0 ? "?" : hop.By)}",
                    $"Дата: {hop.Date?.ToString("O") ?? "не распознана"}{delay}\n{hop.Raw}"));
            }
            return rows;
        }
    }
}
