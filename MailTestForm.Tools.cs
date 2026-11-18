using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using mailTester.Mail;

namespace mailTester
{
    public partial class MailTestForm
    {
        private ComboBox toolMode, toolTlsMode;
        private TextBox toolTarget, toolSender, toolHelo, toolResolver, toolFile, toolPort;
        private Label toolTargetLabel, toolSenderLabel, toolHeloLabel, toolPortLabel, toolTlsLabel, toolHelp, toolsState, toolsSummary;
        private Button bToolsRun, bToolsFile, bToolsCopy, bToolsExport;
        private RichTextBox toolsResult, toolsLog;
        private string toolsReport = "";

        private void BuildTools()
        {
            var layout = Table(1, 3, Canvas);
            Col(layout, 100, true); Row(layout, 232); FillRow(layout); Row(layout, 116);
            AttachScrollablePage(pageTools, layout, 590);
            var input = Card(3, 7, new Padding(0, 0, 0, 10));
            Col(input, 40, true); Col(input, 36, true); Col(input, 24, true);
            Row(input, 21); Row(input, 32); Row(input, 20); Row(input, 32); Row(input, 20); Row(input, 32); FillRow(input);
            toolMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 8, 4) };
            toolMode.Items.AddRange(new object[] { "SPF / SMTP client", "EML / DKIM / DMARC", "PTR / IP Blacklist", "TLS / SSL / HTTPS", "WHOIS / RDAP", "DNS / записи имени", "Комплексный аудит домена" });
            toolTarget = Edit();
            toolResolver = Edit("Системный DNS");
            toolTargetLabel = TextLabel("IP / домен / имя", 9, Muted, true);
            Span(input, TextLabel("Инструмент", 9, Muted, true), 0, 0);
            Span(input, toolMode, 0, 1);
            Span(input, toolTargetLabel, 1, 0); Span(input, toolTarget, 1, 1);
            Field(input, 2, 0, "DNS-резолвер (IP)", toolResolver);
            toolSender = Edit("SMTP envelope sender");
            toolHelo = Edit("EHLO / HELO");
            toolSenderLabel = TextLabel("MAIL FROM", 9, Muted, true);
            toolHeloLabel = TextLabel("HELO (при <> обязателен)", 9, Muted, true);
            Span(input, toolSenderLabel, 0, 2); Span(input, toolSender, 0, 3);
            Span(input, toolHeloLabel, 1, 2); Span(input, toolHelo, 1, 3);
            toolTlsMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 3, 8, 4) };
            toolTlsMode.Items.AddRange(new object[] { "HTTPS / TLS", "SMTP STARTTLS", "SMTP TLS", "POP3 STARTTLS", "POP3 TLS", "IMAP STARTTLS", "IMAP TLS" });
            toolTlsMode.SelectedIndex = 0;
            toolTlsLabel = TextLabel("Режим TLS", 9, Muted, true);
            Span(input, toolTlsLabel, 2, 2); Span(input, toolTlsMode, 2, 3);
            toolPortLabel = TextLabel("TCP-порт", 9, Muted, true);
            toolPort = Edit("443", "443");
            Span(input, toolPortLabel, 2, 4); Span(input, toolPort, 2, 5);
            toolTlsMode.SelectedIndexChanged += (_, _) => toolPort.Text = TlsDiagnostics.DefaultPort((TlsMode)toolTlsMode.SelectedIndex).ToString();
            toolFile = Edit("Выберите оригинальный .eml"); toolFile.ReadOnly = true;
            Span(input, TextLabel("EML-файл", 9, Muted, true), 0, 4);
            Span(input, toolFile, 0, 5);
            bToolsFile = MakeButton("Открыть EML", false);
            bToolsFile.Click += (_, _) => {
                using var dialog = new OpenFileDialog { Filter = "Email (*.eml)|*.eml|Все файлы (*.*)|*.*", CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) toolFile.Text = dialog.FileName;
            };
            Span(input, bToolsFile, 1, 5);
            toolHelp = TextLabel("", 9, Muted);
            Span(input, toolHelp, 0, 6, 2);
            bToolsRun = MakeButton("Проверить"); bToolsRun.Click += ToolsRun_Click;
            Span(input, bToolsRun, 2, 6);
            Span(layout, input, 0, 0);

            var report = Card(1, 3, new Padding(0, 0, 0, 10));
            Col(report, 100, true); Row(report, 40); Row(report, 27); FillRow(report);
            var toolbar = Table(3, 1, Surface);
            Col(toolbar, 100, true); Col(toolbar, 125); Col(toolbar, 125); FillRow(toolbar);
            Span(toolbar, TextLabel("Результаты и рекомендации", 14, Ink, true), 0, 0);
            bToolsCopy = MakeButton("Копировать", false); bToolsExport = MakeButton("Экспорт TXT", false);
            bToolsCopy.Enabled = bToolsExport.Enabled = false;
            bToolsCopy.Click += (_, _) => {
                if (toolsReport.Length == 0) return;
                try { Clipboard.SetText(toolsReport); } catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "Буфер обмена занят."); }
            };
            bToolsExport.Click += (_, _) => {
                if (toolsReport.Length == 0) return;
                using var dialog = new SaveFileDialog { Filter = "Текст (*.txt)|*.txt", FileName = $"mailtester-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.txt", AddExtension = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, toolsReport, Encoding.UTF8); }
                catch (Exception ex) { MessageBox.Show(this, "Ошибка экспорта: " + ex.GetType().Name); }
            };
            Span(toolbar, bToolsCopy, 1, 0); Span(toolbar, bToolsExport, 2, 0);
            Span(report, toolbar, 0, 0);
            toolsSummary = TextLabel("Выберите инструмент и запустите проверку.", 10, Muted);
            Span(report, toolsSummary, 0, 1);
            toolsResult = TextArea(true);
            Span(report, toolsResult, 0, 2);
            Span(layout, report, 0, 1);
            var log = Card(1, 2, Padding.Empty);
            Col(log, 100, true); Row(log, 30); FillRow(log);
            toolsState = TextLabel("Журнал операции", 13, Ink, true); toolsLog = TextArea(true, true);
            Span(log, toolsState, 0, 0); Span(log, toolsLog, 0, 1);
            Span(layout, log, 0, 2);
            toolMode.SelectedIndexChanged += (_, _) => SetToolFields();
            toolMode.SelectedIndex = 0;
        }

        private void SetToolFields()
        {
            int mode = toolMode.SelectedIndex;
            bool auth = mode == 0 || mode == 1;
            bool tls = mode == 3;
            toolTarget.Enabled = true;
            toolSender.Enabled = auth || mode == 6;
            toolHelo.Enabled = auth;
            toolSenderLabel.Text = mode == 6 ? "DKIM selectors (необязательно)" : "MAIL FROM";
            toolSender.PlaceholderText = mode == 6 ? "selector1, selector2" : "SMTP envelope sender";
            toolFile.Enabled = bToolsFile.Enabled = mode == 1;
            toolTlsMode.Enabled = toolPort.Enabled = tls;
            toolResolver.Enabled = mode is 0 or 1 or 2 or 5 or 6;
            toolTargetLabel.Text = mode is 0 or 1 or 2 ? "IP SMTP-клиента" : mode == 3 ? "Имя сервера / сайта" : mode is 4 or 6 ? "Зарегистрированный домен" : "Точное имя записи";
            toolTarget.PlaceholderText = mode is 0 or 1 or 2 ? "192.0.2.10" : mode == 3 ? "www.example.com" : "example.com";
            toolHelp.Text = mode switch {
                0 => "Укажите IP SMTP-клиента, MAIL FROM и HELO (для <> обязательно).",
                1 => "Криптопроверка EML; для SPF введите доверенные SMTP-данные.",
                2 => "PTR/FCrDNS + публичные DNSBL (IPv4). IP отправляется операторам списков.",
                3 => "Проверка TLS-сертификата и цепочки; без авторизации.",
                4 => "RDAP IANA, при отсутствии WHOIS; домен передаётся реестру.",
                6 => "DNS, WHOIS, TLS сайта и MX, обратный DNS; проверяйте свой домен.",
                _ => "Запросы известных типов для имени, а не перечисление всех поддоменов."
            };
        }

        private async void ToolsRun_Click(object sender, EventArgs args)
        {
            if (operation != null) { if (activeButton == bToolsRun) operation.Cancel(); return; }
            int mode = toolMode.SelectedIndex;
            string target = toolTarget.Text.Trim(), senderAddress = toolSender.Text.Trim(), helo = toolHelo.Text.Trim(), file = toolFile.Text;
            int port = TlsDiagnostics.DefaultPort((TlsMode)toolTlsMode.SelectedIndex);
            var tlsMode = (TlsMode)toolTlsMode.SelectedIndex;
            IPAddress ip = null, resolver = null;
            try
            {
                if ((mode == 0 || mode == 2 || (mode == 1 && target.Length > 0)) && !IPAddress.TryParse(target, out ip))
                    throw new ArgumentException("Введите корректный IP-адрес.");
                if (mode == 0 && senderAddress.Length == 0) throw new ArgumentException("Введите SMTP MAIL FROM (или <>).");
                if (mode == 1 && !File.Exists(file)) throw new ArgumentException("Выберите EML-файл.");
                if (mode == 3) {
                    target = DnsPolicyChecks.NormalizeDomain(target);
                    if (!int.TryParse(toolPort.Text, out port) || port < 1 || port > 65535) throw new ArgumentException("Некорректный порт.");
                }
                if (mode is 4 or 6) target = DnsPolicyChecks.NormalizeDomain(target);
                if (mode == 6) DnsPolicyChecks.ParseSelectors(senderAddress);
                if (mode == 5 && target.Length == 0) throw new ArgumentException("Введите DNS-имя.");
                if (toolResolver.Enabled && !string.IsNullOrWhiteSpace(toolResolver.Text) && !IPAddress.TryParse(toolResolver.Text, out resolver))
                    throw new ArgumentException("Некорректный IP DNS-резолвера.");
            }
            catch (ArgumentException ex) { Log(toolsLog, ex.Message); return; }

            toolsReport = ""; toolsResult.Clear(); bToolsCopy.Enabled = bToolsExport.Enabled = false;
            toolsSummary.Text = "Выполняется проверка…";
            var fields = new Control[] { toolMode, toolTarget, toolSender, toolHelo, toolResolver, toolFile, toolPort, toolTlsMode, bToolsFile };
            foreach (var field in fields) field.Enabled = false;
            try
            {
                await RunOperation(bToolsRun, toolsLog, async (ct, progress) => {
                    IReadOnlyList<CheckFinding> findings = await Task.Run(async () => {
                        if (mode == 3) return await TlsDiagnostics.InspectAsync(target, port, tlsMode, ct);
                        if (mode == 4) return await RegistrationDiagnostics.InspectAsync(target, progress, ct);
                        var engine = new DnsDiagnosticEngine(resolver);
                        return mode switch {
                            0 => await engine.CheckSpfAsync(ip, senderAddress, helo, ct),
                            1 => await engine.AnalyzeEmlAsync(file, ip, senderAddress, helo, progress, ct),
                            2 => await engine.CheckIpAsync(ip, true, progress, ct),
                            6 => await engine.AuditDomainAsync(target, DnsPolicyChecks.ParseSelectors(senderAddress), progress, ct),
                            _ => await engine.LookupRecordsAsync(target, ct)
                        };
                    }, ct);
                    var sb = new StringBuilder();
                    sb.AppendLine($"MailTester 1.5 | {toolMode.Text} | {DateTimeOffset.Now:O}");
                    sb.AppendLine("Резолвер (для DNS/SPF): " + (resolver?.ToString() ?? "системный"));
                    sb.AppendLine("Результаты относятся к наблюдаемым данным; это не гарантия доставки.\n");
                    foreach (var item in findings)
                    {
                        sb.AppendLine($"[{item.Level}] {item.Section} | {item.Name}\n{item.Summary}\n{item.Detail}");
                        if (item.Section != "EML") sb.AppendLine("Рекомендации: " + DiagnosticAdvice.For(item, mode == 6 ? target : null));
                        sb.AppendLine();
                    }
                    toolsReport = sb.ToString(); toolsResult.Text = toolsReport;
                    int warnings = findings.Count(x => x.Level == CheckLevel.Warning), errors = findings.Count(x => x.Level == CheckLevel.Error);
                    toolsSummary.Text = $"Результатов: {findings.Count} | предупреждений: {warnings} | ошибок: {errors}";
                    Log(toolsLog, $"Проверка завершена: {findings.Count} результатов, предупреждений {warnings}, ошибок {errors}");
                });
            }
            finally
            {
                toolMode.Enabled = true;
                SetToolFields();
                if (toolsReport.Length == 0) toolsSummary.Text = "Проверка не завершена, смотрите журнал.";
                bToolsCopy.Enabled = bToolsExport.Enabled = toolsReport.Length > 0;
            }
        }
    }
}
