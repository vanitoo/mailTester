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

        public MailTestForm()
        {
            InitializeComponent();
            ConfigureControls();
            FormClosing += (_, e) => {
                if (operation != null)
                {
                    e.Cancel = true;
                    operation.Cancel();
                }
            };
            FormClosed += (_, _) => {
                foreach (var message in messages) message.Dispose();
                tbPassword.Clear();
                tbPop3Pass.Clear();
            };
        }

        private void ConfigureControls()
        {
            pageSend.Controls.Remove(cbSSL);
            cbSSL.Dispose();
            pageGet.Controls.Remove(cbPop3SSL);
            cbPop3SSL.Dispose();
            SetupSecurity(smtpSecurity, pageSend, new Point(389, 5), 0);
            SetupSecurity(receiveSecurity, pageGet, new Point(389, 5), 1);

            receiveProtocol.DropDownStyle = ComboBoxStyle.DropDownList;
            receiveProtocol.Items.AddRange(new object[] { "POP3", "IMAP" });
            receiveProtocol.Location = new Point(6, 57);
            receiveProtocol.Size = new Size(90, 23);
            receiveProtocol.SelectedIndex = 0;
            pageGet.Controls.Add(receiveProtocol);
            var limitLabel = new Label { Text = "Последних писем:", AutoSize = true, Location = new Point(105, 61) };
            pageGet.Controls.Add(limitLabel);
            messageLimit.Location = new Point(230, 57);
            messageLimit.Size = new Size(65, 23);
            messageLimit.Minimum = 1;
            messageLimit.Maximum = 200;
            messageLimit.Value = 25;
            pageGet.Controls.Add(messageLimit);
            splitContainer1.Top += 28;
            splitContainer1.Height -= 28;
            rtbPop3Message.ReadOnly = true;
            rtbPop3Message.DetectUrls = false;
            dgvMessages.MultiSelect = false;
            dgvMessages.AllowUserToAddRows = false;
            bSaveAttachment.Visible = false;
            openAttachmentDialog.Multiselect = true;
            MinimumSize = new Size(550, 400);
            smtpSecurity.SelectedIndexChanged += (_, _) => tbPort.Text = smtpSecurity.SelectedIndex switch { 0 => "587", 1 => "465", _ => "25" };
            receiveSecurity.SelectedIndexChanged += (_, _) => UpdateReceivePort();
            receiveProtocol.SelectedIndexChanged += (_, _) => UpdateReceivePort();
        }

        private static void SetupSecurity(ComboBox box, TabPage page, Point location, int selected)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.AddRange(new object[] { "STARTTLS", "SSL/TLS", "Без TLS" });
            box.Location = location;
            box.Size = new Size(125, 23);
            box.SelectedIndex = selected;
            page.Controls.Add(box);
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

        private static void Log(RichTextBox target, string message)
        {
            target.AppendText($"{DateTime.Now:HH:mm:ss} | {message}\n");
            target.SelectionStart = target.TextLength;
            target.ScrollToCaret();
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
            attachments = openAttachmentDialog.FileNames;
            tbAttachment.Text = string.Join("; ", attachments.Select(Path.GetFileName));
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
            var otherButtons = new[] { bSend, bGetMail, bSaveAttachment, bAttachment }.Where(b => b != button).ToArray();
            foreach (var b in otherButtons) b.Enabled = false;
            button.Text = "Отмена";
            try
            {
                var progress = new Progress<string>(line => { if (!IsDisposed) Log(status, line); });
                await action(cts.Token, progress);
            }
            catch (OperationCanceledException) { Log(status, "Операция отменена или истёк таймаут. Для SMTP при отмене во время передачи статус принятия может быть неизвестен."); }
            catch (MailDiagnosticException ex) { Log(status, ex.Message); }
            catch (Exception ex) { Log(status, "Ошибка: " + ex.GetType().Name); }
            finally
            {
                operation = null;
                activeButton = null;
                button.Text = originalText;
                foreach (var b in otherButtons) b.Enabled = true;
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
            await RunOperation(bSend, rtbStatus, (ct, progress) =>
                Task.Run(() => MailEngine.SendAsync(connection, from, to, subject, body, files, progress, ct), ct));
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
                dgvMessages.DataSource = messages;
                foreach (var message in previous) message.Dispose();
                Log(rtbPop3Status, $"Готово. Получено: {messages.Count}.");
            });
        }

        private void dgvMessages_SelectionChanged(object sender, EventArgs e)
        {
            var selected = dgvMessages.SelectedRows.Count > 0 ? dgvMessages.SelectedRows[0].DataBoundItem as ReceivedMessage : null;
            rtbPop3Message.Text = selected?.Body ?? "";
            bSaveAttachment.Visible = selected?.HasAttachment == true;
        }

        private void MailTestForm_Load(object sender, EventArgs e)
        {
            dgvMessages.AutoGenerateColumns = false;
            dgvMessages.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "From", HeaderText = "Отправитель", Name = "From" });
            dgvMessages.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Subject", HeaderText = "Тема", Name = "Subject" });
            dgvMessages.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Date", HeaderText = "Дата", Name = "Date" });
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
