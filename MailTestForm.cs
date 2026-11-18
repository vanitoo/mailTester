using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net.Mail;
using System.Net;
using System.IO;
using Tbc.Common.Mail.Pop3;
using Tbc.Common.Mail.Pop3.Mime;

namespace mailTester {
    public partial class MailTestForm : Form {
        public MailTestForm() {
            InitializeComponent();
        }

        private void bAttachment_Click(object sender, EventArgs e) {
            var dialog = openAttachmentDialog.ShowDialog();
            tbAttachment.Text = openAttachmentDialog.FileName;
            if (dialog == DialogResult.Cancel) tbAttachment.Text = "";
        }

        private void bSend_Click(object sender, EventArgs e) {
            try {
                if (!ConfirmPlaintext(cbSSL.Checked)) return;
                sendMail(tbSender.Text, tbRecipient.Text, tbSubject.Text, rtbMessage.Text, tbAttachment.Text, tbServer.Text, int.Parse(tbPort.Text), tbLogin.Text, tbPassword.Text, cbSSL.Checked);
                rtbStatus.AppendText(string.Format("{0} | Сообщение '{1}' отправлено\n", DateTime.Now, tbSubject.Text));
            }
            catch (Exception exp) {
                rtbStatus.AppendText(string.Format("{0} | Ошибка: {1}\n", DateTime.Now, Redact(exp.Message, tbPassword.Text)));
            }
        }

        private bool ConfirmPlaintext(bool encrypted) {
            return encrypted || MessageBox.Show(this,
                "Шифрование отключено. Логин, пароль и данные письма могут передаваться открытым текстом. Продолжить?",
                "Небезопасное подключение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private static string Redact(string message, string password) {
            if (string.IsNullOrEmpty(password) || message == null) return message;
            return message.Replace(password, "[REDACTED]")
                .Replace(Convert.ToBase64String(Encoding.UTF8.GetBytes(password)), "[REDACTED]");
        }

        private static void ValidatePort(int port) {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        }

        public static void sendMail(string from, string to, string subject, string message, string attachmentPath, string host, int port, string userName, string pass, bool startTls = true) {
            ValidatePort(port);
            if (port == 465)
                throw new NotSupportedException("SMTP implicit TLS (465) пока не поддерживается. Используйте STARTTLS, например порт 587.");
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(pass))
                throw new ArgumentException("Заполните отправителя, получателя, сервер, логин и пароль.");
            using (var mailMessage = new MailMessage(from, to, subject, message))
            using (var smtpClient = new SmtpClient(host, port)) {
                mailMessage.SubjectEncoding = Encoding.UTF8;
                mailMessage.BodyEncoding = Encoding.UTF8;
                if (!string.IsNullOrWhiteSpace(attachmentPath))
                    mailMessage.Attachments.Add(new Attachment(attachmentPath));
                smtpClient.UseDefaultCredentials = false;
                smtpClient.Credentials = new NetworkCredential(userName, pass);
                smtpClient.EnableSsl = startTls;
                smtpClient.Timeout = 60000;
                smtpClient.Send(mailMessage);
            }
        }

        private void bGetMail_Click(object sender, EventArgs e) {
            try {
                if (!ConfirmPlaintext(cbPop3SSL.Checked)) return;
                var messages = getMessages(tbPop3Server.Text, int.Parse(tbPop3Port.Text), cbPop3SSL.Checked, tbPop3Login.Text, tbPop3Pass.Text);
                rtbPop3Status.AppendText(string.Format("{0} | Получено сообщений: {1}\n", DateTime.Now, messages.Count));
                var previous = dgvMessages.DataSource as IList<RxMailMessage>;
                dgvMessages.DataSource = messages;
                if (previous != null)
                    foreach (var message in previous) message.Dispose();
            }
            catch (Exception exp) {
                rtbPop3Status.AppendText(string.Format("{0} | Ошибка: {1}\n", DateTime.Now, Redact(exp.Message, tbPop3Pass.Text)));
            }

        }

        public static IList<RxMailMessage> getMessages(string Server, int Port, bool SSL, string UserName, string Passw) {
            ValidatePort(Port);
            if (string.IsNullOrWhiteSpace(Server) || string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Passw))
                throw new ArgumentException("Заполните сервер, логин и пароль.");
            if (Port == 995 && !SSL)
                throw new ArgumentException("Порт 995 требует SSL/TLS.");
            var messages = new List<RxMailMessage>();
            using (var client = new Pop3MimeClient(Server, Port, SSL, UserName, Passw)) {
                client.ReadTimeout = 60000;
                try {
                    client.Connect();
                    if (!client.GetMailboxStats(out int count, out int size))
                        throw new InvalidOperationException("Не удалось получить статистику почтового ящика.");
                    for (int i = count; i > 0; i--) {
                        if (!client.GetEmail(i, out var message) || message == null) {
                            message?.Dispose();
                            throw new InvalidOperationException("Не удалось получить письмо № " + i);
                        }
                        message.HasAttachment = message.Attachments.Count > 0;
                        messages.Add(message);
                    }
                    client.Disconnect();
                    return messages;
                }
                catch {
                    foreach (var message in messages) message.Dispose();
                    throw;
                }
            }
        }

        private void dgvMessages_SelectionChanged(object sender, EventArgs e) {
            if (dgvMessages.SelectedRows.Count <= 0) return;
            rtbPop3Message.Text = Convert.ToString(dgvMessages.SelectedRows[0].Cells["Body"].Value);
            bSaveAttachment.Visible = (bool)dgvMessages.SelectedRows[0].Cells["HasAttachment"].Value;
        }

        private void MailTestForm_Load(object sender, EventArgs e) {
            dgvMessages.AutoGenerateColumns = false;
            dgvMessages.Columns.Add(new DataGridViewColumn() {
                DataPropertyName = "From",
                HeaderText = "Отправитель",
                CellTemplate = new DataGridViewTextBoxCell(),
                Name = "From"
            });
            dgvMessages.Columns.Add(new DataGridViewColumn() {
                DataPropertyName = "Subject",
                HeaderText = "Тема",
                CellTemplate = new DataGridViewTextBoxCell(),
                Name = "Subject"
            });
            dgvMessages.Columns.Add(new DataGridViewColumn() {
                DataPropertyName = "HasAttachment",
                HeaderText = "Вложение",
                CellTemplate = new DataGridViewCheckBoxCell(),
                Name = "HasAttachment",
                Visible = false
            });
            dgvMessages.Columns.Add(new DataGridViewColumn() {
                DataPropertyName = "Body",
                HeaderText = "Сообщение",
                CellTemplate = new DataGridViewTextBoxCell(),
                Visible = false,
                Name = "Body"
            });
        }

        private void bSaveAttachment_Click(object sender, EventArgs e) {
            if (dgvMessages.SelectedRows.Count == 0) return;
            var dialog = selectAttachmentFolder.ShowDialog();
            if (dialog == DialogResult.Cancel) return;
            var message = (RxMailMessage)dgvMessages.SelectedRows[0].DataBoundItem;
            string errText = default(string);
            bool saved = message.SaveAttachments(selectAttachmentFolder.SelectedPath, out errText);
            var logText = saved
                ? string.Format("{0} | В каталог {1} Сохранено вложений {2} : {3}\n",
                    DateTime.Now, selectAttachmentFolder.SelectedPath, message.Attachments.Count,
                    string.Join(", ", message.Attachments.Select(m => m.Name).ToArray()))
                : string.Format("{0} | Ошибка: {1}\n", DateTime.Now, errText);
            rtbPop3Status.AppendText(logText);
        }
    }
}
