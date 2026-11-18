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
                sendMail(tbSender.Text, tbRecipient.Text, tbSubject.Text, rtbMessage.Text, tbAttachment.Text, tbServer.Text, int.Parse(tbPort.Text), tbLogin.Text, tbPassword.Text);
                rtbStatus.AppendText(string.Format("{0} | Сообщение '{1}' отправлено\n", DateTime.Now, tbSubject.Text));
            }
            catch (Exception exp) {
                rtbStatus.AppendText(string.Format("{0} | Ошибка: {1}\n", DateTime.Now, exp.Message));
            }
        }

        public static void sendMail(string from, string to, string subject, string message, string attachmentPath, string host, int port, string userName, string pass) {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || string.IsNullOrEmpty(host) || string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(pass))
                throw new Exception("Одно из ключевых полей осталось не заполненым");
            MailMessage mailMessage = new MailMessage(from, to, subject, message);
            mailMessage.SubjectEncoding = Encoding.UTF8;
            if (!string.IsNullOrEmpty(attachmentPath)) {
                var reader = new FileStream(attachmentPath, FileMode.Open, FileAccess.Read);
                var attachmentName = attachmentPath.Split(new char[] { '\\', }, StringSplitOptions.RemoveEmptyEntries).Last();
                mailMessage.Attachments.Add(new Attachment(reader, attachmentName));
            }
            SmtpClient smtpClient = new SmtpClient(host, port) {
                Credentials = new NetworkCredential(userName, pass)
            };
            if (port == 587)
                smtpClient.EnableSsl = true;
            else {
                smtpClient.EnableSsl = false;
                smtpClient.UseDefaultCredentials = false;
            }
            smtpClient.Send(mailMessage);
            mailMessage.Dispose();
        }

        private void bGetMail_Click(object sender, EventArgs e) {
            try {
                var messages = getMessages(tbPop3Server.Text, int.Parse(tbPop3Port.Text), cbPop3SSL.Checked, tbPop3Login.Text, tbPop3Pass.Text);
                rtbPop3Status.AppendText(string.Format("{0} | Получено сообщений: {1}\n", DateTime.Now, messages.Count));
                dgvMessages.DataSource = messages;
            }
            catch (Exception exp) {
                rtbPop3Status.AppendText(string.Format("{0} | Ошибка: {1}\n", DateTime.Now, exp.Message));
            }

        }

        public static IList<RxMailMessage> getMessages(string Server, int Port, bool SSL, string UserName, string Passw) {
            Pop3 pop = new Pop3(Server, Port, SSL, UserName, Passw);
            if (SSL)
            {
                pop.
            }
            pop.Connect();
            pop.GetMailboxStats();
            var messages = new List<RxMailMessage>();
            for (int i = pop.NumberOfMailsInMailbox; i > 0; i--) {
                var message = new RxMailMessage();
                pop.RecieveMessage(i, out message);
                message.HasAttachment = message.Attachments.Count > 0 ? true : false;
                messages.Add(message);
            }
            pop.Disconnect();
            return messages;
        }

        private void dgvMessages_SelectionChanged(object sender, EventArgs e) {
            if (dgvMessages.SelectedRows.Count <= 0) return;
            rtbPop3Message.Text = dgvMessages.SelectedRows[0].Cells["Body"].Value.ToString();
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
            var dialog = selectAttachmentFolder.ShowDialog();
            if (dialog == DialogResult.Cancel) return;
            var message = (RxMailMessage)dgvMessages.SelectedRows[0].DataBoundItem;
            string errText = default(string);
            message.SaveAttachments(selectAttachmentFolder.SelectedPath, out errText);
            var logText = string.IsNullOrEmpty(errText)
                ? string.Format("{0} | В каталог {1} Сохранено вложений {2} : {3}\n",
                    DateTime.Now, selectAttachmentFolder.SelectedPath, message.Attachments.Count,
                    string.Join(", ", message.Attachments.Select(m => m.Name).ToArray()))
                : string.Format("{0} | Ошибка: {1}\n", DateTime.Now, errText);
            rtbPop3Status.AppendText(logText);
        }
    }
}
