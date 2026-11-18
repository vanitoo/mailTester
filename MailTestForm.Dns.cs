using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using mailTester.Mail;

namespace mailTester
{
    public partial class MailTestForm
    {
        private TextBox dnsDomain, dnsSelectors, dnsResolver;
        private Button bDnsRun, bDnsExport, bDnsCopy;
        private RichTextBox dnsDetail, dnsLog;
        private DataGridView dnsGrid;
        private Label dnsState, dnsSummary;
        private DnsDiagnosticReport dnsReport;

        private void BuildDns()
        {
            var layout = Table(1, 3, Canvas);
            Col(layout, 100, true); Row(layout, 150); FillRow(layout); Row(layout, 130);
            AttachScrollablePage(pageDns, layout, 530);

            var input = Card(3, 4, new Padding(0, 0, 0, 10));
            Col(input, 40, true); Col(input, 38, true); Col(input, 22, true);
            Row(input, 29); Row(input, 20); Row(input, 34); FillRow(input);
            Span(input, TextLabel("01  /  Домен и резолвер", 14, Ink, true), 0, 0, 3);
            dnsDomain = Edit("example.com");
            dnsSelectors = Edit("selector1, selector2 (необязательно)");
            dnsResolver = Edit("Системный DNS");
            Field(input, 0, 1, "Домен (d= для DKIM)", dnsDomain);
            Field(input, 1, 1, "DKIM-селекторы (s=)", dnsSelectors);
            Field(input, 2, 1, "DNS-сервер (IP)", dnsResolver);
            var tip = TextLabel("Проверяются опубликованные записи, не доставка конкретного письма.", 9, Muted);
            Span(input, tip, 0, 3, 2);
            bDnsRun = MakeButton("Проверить");
            bDnsRun.Click += DnsRun_Click;
            Span(input, bDnsRun, 2, 3);
            Span(layout, input, 0, 0);

            var result = Card(1, 3, new Padding(0, 0, 0, 10));
            Col(result, 100, true); Row(result, 36); Row(result, 30); FillRow(result);
            var toolbar = Table(3, 1, Surface);
            Col(toolbar, 100, true); Col(toolbar, 120); Col(toolbar, 120); FillRow(toolbar);
            Span(toolbar, TextLabel("02  /  Результаты", 14, Ink, true), 0, 0);
            bDnsCopy = MakeButton("Копировать", false);
            bDnsExport = MakeButton("Экспорт TXT", false);
            bDnsCopy.Enabled = bDnsExport.Enabled = false;
            bDnsCopy.Click += (_, _) => CopyDnsReport();
            bDnsExport.Click += (_, _) => ExportDnsReport();
            Span(toolbar, bDnsCopy, 1, 0); Span(toolbar, bDnsExport, 2, 0);
            Span(result, toolbar, 0, 0);
            dnsSummary = TextLabel("Введите домен и нажмите «Проверить».", 10, Muted);
            Span(result, dnsSummary, 0, 1);

            var panes = Table(1, 2, BorderColor);
            Col(panes, 100, true);
            panes.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            panes.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            dnsGrid = new DataGridView {
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), BackgroundColor = Surface,
                BorderStyle = BorderStyle.None, GridColor = BorderColor, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                AutoGenerateColumns = false, MultiSelect = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30, RowTemplate = { Height = 30 }
            };
            dnsGrid.ColumnHeadersDefaultCellStyle.BackColor = Canvas;
            dnsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            dnsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            dnsGrid.DefaultCellStyle.SelectionForeColor = Ink;
            dnsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Section", HeaderText = "Тип", FillWeight = 12 });
            dnsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Имя", FillWeight = 32 });
            dnsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Level", HeaderText = "Статус", FillWeight = 15 });
            dnsGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Summary", HeaderText = "Результат", FillWeight = 41 });
            dnsGrid.CellFormatting += (_, args) => {
                if (args.RowIndex < 0 || args.ColumnIndex != 2 || dnsGrid.Rows[args.RowIndex].DataBoundItem is not CheckFinding finding) return;
                args.CellStyle.ForeColor = finding.Level switch {
                    CheckLevel.Ok => Green, CheckLevel.Error => Red, CheckLevel.Warning => Color.FromArgb(161, 98, 7), _ => Muted
                };
            };
            dnsGrid.SelectionChanged += (_, _) => {
                if (dnsDetail != null)
                    dnsDetail.Text = dnsGrid.SelectedRows.Count > 0 && dnsGrid.SelectedRows[0].DataBoundItem is CheckFinding finding
                        ? finding.Name + "\n" + finding.Summary + "\n\n" + finding.Detail : "";
            };
            Span(panes, dnsGrid, 0, 0);
            dnsDetail = TextArea(true);
            Span(panes, dnsDetail, 0, 1);
            Span(result, panes, 0, 2);
            Span(layout, result, 0, 1);
            var log = Card(1, 2, Padding.Empty);
            Col(log, 100, true); Row(log, 30); FillRow(log);
            dnsState = TextLabel("03  /  Диагностика DNS", 13, Ink, true);
            dnsLog = TextArea(true, true);
            Span(log, dnsState, 0, 0); Span(log, dnsLog, 0, 1);
            Span(layout, log, 0, 2);
        }

        private async void DnsRun_Click(object sender, EventArgs args)
        {
            if (operation != null) { if (activeButton == bDnsRun) operation.Cancel(); return; }
            string domain;
            string[] selectors;
            IPAddress server = null;
            try
            {
                domain = DnsPolicyChecks.NormalizeDomain(dnsDomain.Text);
                selectors = DnsPolicyChecks.ParseSelectors(dnsSelectors.Text);
                if (!string.IsNullOrWhiteSpace(dnsResolver.Text) && !IPAddress.TryParse(dnsResolver.Text.Trim(), out server))
                    throw new ArgumentException("Укажите IPv4/IPv6 резолвера или оставьте поле пустым.");
                if (selectors.Any(x => x.Length + domain.Length + 12 > 253) || domain.Length + 7 > 253)
                    throw new ArgumentException("Слишком длинное DNS-имя.");
            }
            catch (ArgumentException ex) { Log(dnsLog, ex.Message); return; }
            dnsReport = null;
            dnsGrid.DataSource = null;
            dnsDetail.Clear();
            dnsSummary.Text = "Проверяется: " + domain;
            bDnsCopy.Enabled = bDnsExport.Enabled = false;
            dnsDomain.Enabled = dnsSelectors.Enabled = dnsResolver.Enabled = false;
            try
            {
                await RunOperation(bDnsRun, dnsLog, async (ct, progress) => {
                    var engine = new DnsDiagnosticEngine(server);
                    var report = await engine.InspectAsync(domain, selectors, progress, ct);
                    dnsReport = report;
                    dnsGrid.DataSource = report.Findings.ToList();
                    int ok = report.Findings.Count(x => x.Level == CheckLevel.Ok);
                    int warning = report.Findings.Count(x => x.Level == CheckLevel.Warning);
                    int errors = report.Findings.Count(x => x.Level == CheckLevel.Error);
                    dnsSummary.Text = $"{domain}  |  OK: {ok}  ·  предупреждений: {warning}  ·  ошибок: {errors}  |  {report.Resolver}";
                    Log(dnsLog, $"DNS-проверка завершена: OK {ok}, предупреждений {warning}, ошибок {errors}");
                });
            }
            finally
            {
                dnsDomain.Enabled = dnsSelectors.Enabled = dnsResolver.Enabled = true;
                if (dnsReport == null) dnsSummary.Text = "Проверка не завершена. Смотрите журнал.";
                bDnsCopy.Enabled = bDnsExport.Enabled = dnsReport != null;
            }
        }

        private string FormatDnsReport()
        {
            var report = dnsReport;
            if (report == null) return "";
            var sb = new StringBuilder();
            sb.AppendLine($"MailTester 1.4 | DNS / Domain");
            sb.AppendLine($"Домен: {report.Domain}\nРезолвер: {report.Resolver}\nДата: {report.Timestamp:O}");
            sb.AppendLine("Статический анализ DNS. Не доказывает доставку или SPF/DKIM/DMARC PASS для письма.\n");
            foreach (var item in report.Findings)
                sb.AppendLine($"[{item.Level}] {item.Section} | {item.Name}\n{item.Summary}\n{item.Detail}\n");
            return sb.ToString();
        }

        private void CopyDnsReport()
        {
            if (dnsReport == null) return;
            try { Clipboard.SetText(FormatDnsReport()); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "Буфер обмена занят."); }
        }

        private void ExportDnsReport()
        {
            if (dnsReport == null) return;
            using var dialog = new SaveFileDialog { Filter = "Текст (*.txt)|*.txt", FileName = "dns-" + dnsReport.Domain + ".txt", AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, FormatDnsReport(), Encoding.UTF8); }
            catch (Exception ex) { MessageBox.Show(this, "Ошибка экспорта: " + ex.GetType().Name); }
        }
    }
}
