using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MailKit.Security;
using mailTester.Mail;

namespace mailTester
{
    public partial class MailTestForm : Form
    {
        private readonly ComboBox smtpSecurity = new ComboBox();
        private readonly ComboBox receiveSecurity = new ComboBox();
        private readonly ComboBox receiveProtocol = new ComboBox();
        private readonly NumericUpDown messageLimit = new NumericUpDown();
        private string[] attachments = Array.Empty<string>();
        private List<ReceivedMessage> messages = new List<ReceivedMessage>();
        private CancellationTokenSource operation;
        private Button activeButton;
        private int sentCount, receivedCount, errorCount;
        private bool closeRequested;

        public MailTestForm()
        {
            InitializeComponent();
            ConfigureControls();
            FormClosing += (_, e) => {
                if (operation != null)
                {
                    e.Cancel = true;
                    closeRequested = true;
                    operation.Cancel();
                }
            };
            FormClosed += (_, _) => {
                foreach (var message in messages) message.Dispose();
                tbPassword.Clear();
                tbPop3Pass.Clear();
                openAttachmentDialog.Dispose();
                selectAttachmentFolder.Dispose();
            };
        }

        private void ConfigureControls()
        {
            SetupSecurity(smtpSecurity, 0);
            SetupSecurity(receiveSecurity, 1);
            receiveProtocol.DropDownStyle = ComboBoxStyle.DropDownList;
            receiveProtocol.Dock = DockStyle.Fill;
            receiveProtocol.Margin = new Padding(0, 3, 8, 4);
            receiveProtocol.Items.AddRange(new object[] { "POP3", "IMAP" });
            receiveProtocol.SelectedIndex = 0;
            messageLimit.Dock = DockStyle.Fill;
            messageLimit.Margin = new Padding(0, 3, 8, 4);
            messageLimit.Minimum = 1;
            messageLimit.Maximum = 200;
            messageLimit.Value = 25;
            smtpSecurity.SelectedIndexChanged += (_, _) => tbPort.Text = smtpSecurity.SelectedIndex switch { 0 => "587", 1 => "465", _ => "25" };
            receiveSecurity.SelectedIndexChanged += (_, _) => UpdateReceivePort();
            receiveProtocol.SelectedIndexChanged += (_, _) => UpdateReceivePort();
            UpdateReceivePort();
            ClearAttachments();
            Log(journalBox, "MailTester 1.5.0 запущен");
        }

        private static void SetupSecurity(ComboBox box, int selected)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Dock = DockStyle.Fill;
            box.Margin = new Padding(0, 3, 8, 4);
            box.Items.AddRange(new object[] { "STARTTLS", "SSL/TLS", "Без TLS" });
            box.SelectedIndex = selected;
        }

        private void UpdateReceivePort()
        {
            bool imap = receiveProtocol.SelectedIndex == 1;
            tbPop3Port.Text = receiveSecurity.SelectedIndex == 1 ? (imap ? "993" : "995") : (imap ? "143" : "110");
        }

        private static SecureSocketOptions Security(ComboBox box) => box.SelectedIndex switch {
            0 => SecureSocketOptions.StartTls,
            1 => SecureSocketOptions.SslOnConnect,
            2 => SecureSocketOptions.None,
            _ => throw new InvalidOperationException("Выберите шифрование.")
        };

        private bool ConfirmPlaintext(SecureSocketOptions security)
        {
            return security != SecureSocketOptions.None || MessageBox.Show(this,
                "Шифрование отключено. Учётные данные и письма могут передаваться открытым текстом. Продолжить?",
                "Небезопасное подключение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void Log(RichTextBox target, string message)
        {
            string prefix = target == rtbStatus ? "SMTP" : target == rtbPop3Status ? "RECV" : target == dnsLog ? "DNS" : target == toolsLog ? "TOOL" : "APP";
            string line = $"{DateTime.Now:HH:mm:ss} | {prefix,-4} | {message}\n";
            AppendLog(target, line);
            if (target != journalBox) AppendLog(journalBox, line);
        }

        private static void AppendLog(RichTextBox target, string line)
        {
            const int maxChars = 200000;
            if (target.TextLength + line.Length > maxChars)
            {
                target.Select(0, Math.Min(target.TextLength, 20000));
                target.SelectedText = "";
            }
            target.AppendText(line);
            target.SelectionStart = target.TextLength;
            target.ScrollToCaret();
        }

        private void CopyJournal()
        {
            if (journalBox.TextLength == 0) return;
            try { Clipboard.SetText(journalBox.Text); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "Буфер обмена занят."); }
        }

        private void ExportJournal()
        {
            using var dialog = new SaveFileDialog { Filter = "Текстовый журнал (*.txt)|*.txt", FileName = $"mailtester-{DateTime.Now:yyyyMMdd-HHmmss}.txt", AddExtension = true, OverwritePrompt = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { File.WriteAllText(dialog.FileName, journalBox.Text, System.Text.Encoding.UTF8); }
            catch (Exception ex) { MessageBox.Show(this, "Не удалось сохранить журнал: " + ex.GetType().Name); }
        }

        private void ClearAttachments()
        {
            if (operation != null) return;
            attachments = Array.Empty<string>();
            UpdateAttachments();
        }

        private void UpdateAttachments()
        {
            tbAttachment.Text = string.Join("; ", attachments.Select(Path.GetFileName));
            attachmentInfo.Text = $"Вложения: {attachments.Length}";
            bClearAttachments.Enabled = operation == null && attachments.Length > 0;
        }

        private ReceivedMessage SelectedMessage() =>
            dgvMessages.SelectedRows.Count > 0 ? dgvMessages.SelectedRows[0].DataBoundItem as ReceivedMessage : null;

        private void ApplyFilter()
        {
            string term = searchMessages.Text.Trim();
            var filtered = messages.Where(m => term.Length == 0 ||
                m.Subject.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                m.From.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
            dgvMessages.DataSource = filtered;
            inboxCount.Text = $"02  /  Письма · {filtered.Count} из {messages.Count}";
            dgvMessages_SelectionChanged(dgvMessages, EventArgs.Empty);
        }

        private void UpdateMetrics()
        {
            metricSent.Text = sentCount.ToString();
            metricReceived.Text = receivedCount.ToString();
            metricErrors.Text = errorCount.ToString();
        }

        private static MailConnection Connection(TextBox host, TextBox port, ComboBox security, TextBox login, TextBox password, bool requireAuth)
        {
            if (!int.TryParse(port.Text, out int value)) throw new ArgumentException("Некорректный порт.");
            var connection = new MailConnection(host.Text, value, Security(security), login.Text, password.Text);
            connection.Validate(requireAuth);
            return connection;
        }

        private void bAttachment_Click(object sender, EventArgs e)
        {
            if (operation != null) return;
            if (openAttachmentDialog.ShowDialog() != DialogResult.OK) return;
            attachments = attachments.Concat(openAttachmentDialog.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            UpdateAttachments();
        }

        private async Task RunOperation(Button button, RichTextBox status, Func<CancellationToken, IProgress<string>, Task> action)
        {
            if (operation != null)
            {
                if (activeButton == button) operation.Cancel();
                return;
            }
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            operation = cts;
            activeButton = button;
            string originalText = button.Text;
            string operationName = button == bSend ? "SMTP" : button == bGetMail ? "Получение" : button == bDnsRun ? "DNS" : button == bToolsRun ? "Инструменты" : "Вложения";
            var otherButtons = new[] { bSend, bGetMail, bSaveAttachment, bAttachment, bClearAttachments, bDnsRun, bToolsRun }.Where(b => b != button).ToArray();
            foreach (var b in otherButtons) b.Enabled = false;
            button.Text = "Отмена";
            bCancelGlobal.Visible = true;
            bClearJournal.Enabled = false;
            statusLabel.Text = "●  Выполняется";
            statusLabel.ForeColor = Accent;
            Label localState = status == rtbStatus ? sendState : status == dnsLog ? dnsState : status == toolsLog ? toolsState : receiveState;
            string originalState = localState.Text;
            localState.Text = "Выполняется: " + operationName;
            string outcome = "Готово";
            Color color = Green;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Log(status, $"Начало операции: {operationName}");
            try
            {
                var progress = new Progress<string>(line => { if (!IsDisposed) Log(status, line); });
                await action(cts.Token, progress);
            }
            catch (OperationCanceledException)
            {
                outcome = "Отменено"; color = Muted;
                Log(status, "Операция отменена или истёк таймаут." + (button == bSend ? " При отмене передачи SMTP статус принятия может быть неизвестен." : ""));
            }
            catch (MailDiagnosticException ex)
            {
                outcome = "Ошибка"; color = Red; errorCount++;
                Log(status, ex.Message);
            }
            catch (Exception ex)
            {
                outcome = "Ошибка"; color = Red; errorCount++;
                Log(status, "Ошибка: " + ex.GetType().Name);
            }
            finally
            {
                watch.Stop();
                operation = null;
                activeButton = null;
                button.Text = originalText;
                bCancelGlobal.Visible = false;
                bClearJournal.Enabled = true;
                foreach (var b in otherButtons) b.Enabled = true;
                bClearAttachments.Enabled = attachments.Length > 0;
                bSaveAttachment.Enabled = SelectedMessage()?.HasAttachment == true;
                statusLabel.Text = "●  " + outcome;
                statusLabel.ForeColor = color;
                localState.Text = originalState;
                metricLast.Text = $"Последняя операция: {operationName} · {outcome} · {watch.Elapsed.TotalSeconds:F1} с";
                UpdateMetrics();
                Log(status, $"{outcome}. Время: {watch.Elapsed.TotalSeconds:F1} с");
                if (closeRequested) BeginInvoke(new Action(Close));
            }
        }

        private async void bSend_Click(object sender, EventArgs e)
        {
            if (operation != null) { if (activeButton == bSend) operation.Cancel(); return; }
            MailConnection connection;
            try { connection = Connection(tbServer, tbPort, smtpSecurity, tbLogin, tbPassword, false); }
            catch (ArgumentException ex) { Log(rtbStatus, ex.Message); return; }
            if (!ConfirmPlaintext(connection.Security)) return;
            string from = tbSender.Text, to = tbRecipient.Text, subject = tbSubject.Text, body = rtbMessage.Text;
            string[] files = attachments.ToArray();
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            { Log(rtbStatus, "Укажите отправителя и получателя."); return; }
            await RunOperation(bSend, rtbStatus, async (ct, progress) => {
                await Task.Run(() => MailEngine.SendAsync(connection, from, to, subject, body, files, progress, ct), ct);
                sentCount++;
            });
        }

        private async void bGetMail_Click(object sender, EventArgs e)
        {
            if (operation != null) { if (activeButton == bGetMail) operation.Cancel(); return; }
            MailConnection connection;
            try { connection = Connection(tbPop3Server, tbPop3Port, receiveSecurity, tbPop3Login, tbPop3Pass, true); }
            catch (ArgumentException ex) { Log(rtbPop3Status, ex.Message); return; }
            if (!ConfirmPlaintext(connection.Security)) return;
            var protocol = receiveProtocol.SelectedIndex == 1 ? ReceiveProtocol.Imap : ReceiveProtocol.Pop3;
            int limit = (int)messageLimit.Value;
            await RunOperation(bGetMail, rtbPop3Status, async (ct, progress) => {
                var result = await Task.Run(() => MailEngine.ReceiveAsync(connection, protocol, limit, progress, ct), ct);
                var previous = messages;
                messages = result;
                receivedCount += messages.Count;
                ApplyFilter();
                foreach (var message in previous) message.Dispose();
                Log(rtbPop3Status, $"Готово. Получено: {messages.Count}.");
            });
        }

        private void dgvMessages_SelectionChanged(object sender, EventArgs e)
        {
            // During control construction the preview does not exist yet.
            if (previewSubject == null || previewMeta == null || rtbPop3Message == null || previewFiles == null || bSaveAttachment == null) return;
            var selected = SelectedMessage();
            previewSubject.Text = selected == null ? "Выберите письмо" : string.IsNullOrWhiteSpace(selected.Subject) ? "(Без темы)" : selected.Subject;
            previewMeta.Text = selected == null ? "Здесь будут данные сообщения" : $"От: {selected.From}\r\nДата: {selected.Date:dd.MM.yyyy HH:mm}";
            rtbPop3Message.Text = selected?.Body ?? "";
            previewFiles.Text = selected == null ? "Вложения: —" : $"Вложения: {selected.Message.Attachments.Count()}";
            bSaveAttachment.Enabled = activeButton == bSaveAttachment || (operation == null && selected?.HasAttachment == true);
        }

        private async void bSaveAttachment_Click(object sender, EventArgs e)
        {
            if (operation != null) { if (activeButton == bSaveAttachment) operation.Cancel(); return; }
            if (dgvMessages.SelectedRows.Count == 0 || dgvMessages.SelectedRows[0].DataBoundItem is not ReceivedMessage message) return;
            if (selectAttachmentFolder.ShowDialog() != DialogResult.OK) return;
            string directory = selectAttachmentFolder.SelectedPath;
            await RunOperation(bSaveAttachment, rtbPop3Status, async (ct, progress) => {
                var saved = await Task.Run(() => AttachmentStore.SaveAsync(message.Message, directory, ct), ct);
                Log(rtbPop3Status, $"Сохранено вложений: {saved.Count}.");
            });
        }
    }
}
