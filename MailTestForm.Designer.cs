namespace mailTester {
    partial class MailTestForm {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            this.openAttachmentDialog = new System.Windows.Forms.OpenFileDialog();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.pageSend = new System.Windows.Forms.TabPage();
            this.lSender = new System.Windows.Forms.Label();
            this.tbSender = new System.Windows.Forms.TextBox();
            this.lRecipient = new System.Windows.Forms.Label();
            this.tbRecipient = new System.Windows.Forms.TextBox();
            this.cbSSL = new System.Windows.Forms.CheckBox();
            this.lPassword = new System.Windows.Forms.Label();
            this.tbPassword = new System.Windows.Forms.TextBox();
            this.lLogin = new System.Windows.Forms.Label();
            this.tbLogin = new System.Windows.Forms.TextBox();
            this.lPort = new System.Windows.Forms.Label();
            this.tbPort = new System.Windows.Forms.TextBox();
            this.lServer = new System.Windows.Forms.Label();
            this.tbServer = new System.Windows.Forms.TextBox();
            this.lSubject = new System.Windows.Forms.Label();
            this.tbSubject = new System.Windows.Forms.TextBox();
            this.bSend = new System.Windows.Forms.Button();
            this.rtbStatus = new System.Windows.Forms.RichTextBox();
            this.bAttachment = new System.Windows.Forms.Button();
            this.tbAttachment = new System.Windows.Forms.TextBox();
            this.rtbMessage = new System.Windows.Forms.RichTextBox();
            this.pageGet = new System.Windows.Forms.TabPage();
            this.rtbPop3Status = new System.Windows.Forms.RichTextBox();
            this.cbPop3SSL = new System.Windows.Forms.CheckBox();
            this.label1 = new System.Windows.Forms.Label();
            this.tbPop3Pass = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.tbPop3Login = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.tbPop3Port = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.tbPop3Server = new System.Windows.Forms.TextBox();
            this.bGetMail = new System.Windows.Forms.Button();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.rtbPop3Message = new System.Windows.Forms.RichTextBox();
            this.dgvMessages = new System.Windows.Forms.DataGridView();
            this.bSaveAttachment = new System.Windows.Forms.Button();
            this.selectAttachmentFolder = new System.Windows.Forms.FolderBrowserDialog();
            this.tabControl1.SuspendLayout();
            this.pageSend.SuspendLayout();
            this.pageGet.SuspendLayout();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvMessages)).BeginInit();
            this.SuspendLayout();
            // 
            // openAttachmentDialog
            // 
            this.openAttachmentDialog.FileName = "*.*";
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.pageSend);
            this.tabControl1.Controls.Add(this.pageGet);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(530, 362);
            this.tabControl1.TabIndex = 11;
            // 
            // pageSend
            // 
            this.pageSend.Controls.Add(this.lSender);
            this.pageSend.Controls.Add(this.tbSender);
            this.pageSend.Controls.Add(this.lRecipient);
            this.pageSend.Controls.Add(this.tbRecipient);
            this.pageSend.Controls.Add(this.cbSSL);
            this.pageSend.Controls.Add(this.lPassword);
            this.pageSend.Controls.Add(this.tbPassword);
            this.pageSend.Controls.Add(this.lLogin);
            this.pageSend.Controls.Add(this.tbLogin);
            this.pageSend.Controls.Add(this.lPort);
            this.pageSend.Controls.Add(this.tbPort);
            this.pageSend.Controls.Add(this.lServer);
            this.pageSend.Controls.Add(this.tbServer);
            this.pageSend.Controls.Add(this.lSubject);
            this.pageSend.Controls.Add(this.tbSubject);
            this.pageSend.Controls.Add(this.bSend);
            this.pageSend.Controls.Add(this.rtbStatus);
            this.pageSend.Controls.Add(this.bAttachment);
            this.pageSend.Controls.Add(this.tbAttachment);
            this.pageSend.Controls.Add(this.rtbMessage);
            this.pageSend.Location = new System.Drawing.Point(4, 22);
            this.pageSend.Name = "pageSend";
            this.pageSend.Padding = new System.Windows.Forms.Padding(3);
            this.pageSend.Size = new System.Drawing.Size(522, 336);
            this.pageSend.TabIndex = 0;
            this.pageSend.Text = "Отправка";
            this.pageSend.UseVisualStyleBackColor = true;
            // 
            // lSender
            // 
            this.lSender.AutoSize = true;
            this.lSender.Location = new System.Drawing.Point(2, 61);
            this.lSender.Name = "lSender";
            this.lSender.Size = new System.Drawing.Size(73, 13);
            this.lSender.TabIndex = 35;
            this.lSender.Text = "Отправитель";
            // 
            // tbSender
            // 
            this.tbSender.Location = new System.Drawing.Point(97, 58);
            this.tbSender.Name = "tbSender";
            this.tbSender.Size = new System.Drawing.Size(420, 20);
            this.tbSender.TabIndex = 34;
            this.tbSender.Text = "ed_test@tbc.su";
            // 
            // lRecipient
            // 
            this.lRecipient.AutoSize = true;
            this.lRecipient.Location = new System.Drawing.Point(2, 87);
            this.lRecipient.Name = "lRecipient";
            this.lRecipient.Size = new System.Drawing.Size(66, 13);
            this.lRecipient.TabIndex = 33;
            this.lRecipient.Text = "Получатель";
            // 
            // tbRecipient
            // 
            this.tbRecipient.Location = new System.Drawing.Point(97, 84);
            this.tbRecipient.Name = "tbRecipient";
            this.tbRecipient.Size = new System.Drawing.Size(420, 20);
            this.tbRecipient.TabIndex = 32;
            this.tbRecipient.Text = "ed_test@tbc.su";
            // 
            // cbSSL
            // 
            this.cbSSL.AutoSize = true;
            this.cbSSL.Checked = true;
            this.cbSSL.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbSSL.Location = new System.Drawing.Point(417, 8);
            this.cbSSL.Name = "cbSSL";
            this.cbSSL.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.cbSSL.Size = new System.Drawing.Size(46, 17);
            this.cbSSL.TabIndex = 31;
            this.cbSSL.Text = "SSL";
            this.cbSSL.UseVisualStyleBackColor = true;
            // 
            // lPassword
            // 
            this.lPassword.AutoSize = true;
            this.lPassword.Location = new System.Drawing.Point(227, 35);
            this.lPassword.Name = "lPassword";
            this.lPassword.Size = new System.Drawing.Size(45, 13);
            this.lPassword.TabIndex = 30;
            this.lPassword.Text = "Пароль";
            // 
            // tbPassword
            // 
            this.tbPassword.Location = new System.Drawing.Point(278, 32);
            this.tbPassword.Name = "tbPassword";
            this.tbPassword.Size = new System.Drawing.Size(100, 20);
            this.tbPassword.TabIndex = 29;
            this.tbPassword.Text = "n9HXu0U3bPwZZLD67z9Z";
            // 
            // lLogin
            // 
            this.lLogin.AutoSize = true;
            this.lLogin.Location = new System.Drawing.Point(3, 35);
            this.lLogin.Name = "lLogin";
            this.lLogin.Size = new System.Drawing.Size(38, 13);
            this.lLogin.TabIndex = 28;
            this.lLogin.Text = "Логин";
            // 
            // tbLogin
            // 
            this.tbLogin.Location = new System.Drawing.Point(98, 32);
            this.tbLogin.Name = "tbLogin";
            this.tbLogin.Size = new System.Drawing.Size(100, 20);
            this.tbLogin.TabIndex = 27;
            this.tbLogin.Text = "ed_test";
            // 
            // lPort
            // 
            this.lPort.AutoSize = true;
            this.lPort.Location = new System.Drawing.Point(227, 9);
            this.lPort.Name = "lPort";
            this.lPort.Size = new System.Drawing.Size(32, 13);
            this.lPort.TabIndex = 26;
            this.lPort.Text = "Порт";
            // 
            // tbPort
            // 
            this.tbPort.Location = new System.Drawing.Point(278, 6);
            this.tbPort.Name = "tbPort";
            this.tbPort.Size = new System.Drawing.Size(100, 20);
            this.tbPort.TabIndex = 25;
            this.tbPort.Text = "587";
            // 
            // lServer
            // 
            this.lServer.AutoSize = true;
            this.lServer.Location = new System.Drawing.Point(3, 9);
            this.lServer.Name = "lServer";
            this.lServer.Size = new System.Drawing.Size(89, 13);
            this.lServer.TabIndex = 24;
            this.lServer.Text = "Адресе сервера";
            // 
            // tbServer
            // 
            this.tbServer.Location = new System.Drawing.Point(98, 6);
            this.tbServer.Name = "tbServer";
            this.tbServer.Size = new System.Drawing.Size(100, 20);
            this.tbServer.TabIndex = 23;
            this.tbServer.Text = "post.tbc.su";
            // 
            // lSubject
            // 
            this.lSubject.AutoSize = true;
            this.lSubject.Location = new System.Drawing.Point(7, 119);
            this.lSubject.Name = "lSubject";
            this.lSubject.Size = new System.Drawing.Size(34, 13);
            this.lSubject.TabIndex = 22;
            this.lSubject.Text = "Тема";
            // 
            // tbSubject
            // 
            this.tbSubject.Location = new System.Drawing.Point(47, 116);
            this.tbSubject.Name = "tbSubject";
            this.tbSubject.Size = new System.Drawing.Size(466, 20);
            this.tbSubject.TabIndex = 21;
            // 
            // bSend
            // 
            this.bSend.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.bSend.Location = new System.Drawing.Point(378, 272);
            this.bSend.Name = "bSend";
            this.bSend.Size = new System.Drawing.Size(137, 55);
            this.bSend.TabIndex = 20;
            this.bSend.Text = "Отправить";
            this.bSend.UseVisualStyleBackColor = true;
            this.bSend.Click += new System.EventHandler(this.bSend_Click);
            // 
            // rtbStatus
            // 
            this.rtbStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbStatus.Location = new System.Drawing.Point(7, 272);
            this.rtbStatus.Name = "rtbStatus";
            this.rtbStatus.ReadOnly = true;
            this.rtbStatus.Size = new System.Drawing.Size(365, 55);
            this.rtbStatus.TabIndex = 19;
            this.rtbStatus.Text = "";
            // 
            // bAttachment
            // 
            this.bAttachment.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.bAttachment.Location = new System.Drawing.Point(7, 243);
            this.bAttachment.Name = "bAttachment";
            this.bAttachment.Size = new System.Drawing.Size(135, 23);
            this.bAttachment.TabIndex = 18;
            this.bAttachment.Text = "Выбрать вложение";
            this.bAttachment.UseVisualStyleBackColor = true;
            this.bAttachment.Click += new System.EventHandler(this.bAttachment_Click);
            // 
            // tbAttachment
            // 
            this.tbAttachment.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.tbAttachment.Location = new System.Drawing.Point(148, 245);
            this.tbAttachment.Name = "tbAttachment";
            this.tbAttachment.ReadOnly = true;
            this.tbAttachment.Size = new System.Drawing.Size(367, 20);
            this.tbAttachment.TabIndex = 17;
            // 
            // rtbMessage
            // 
            this.rtbMessage.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbMessage.Location = new System.Drawing.Point(7, 142);
            this.rtbMessage.Name = "rtbMessage";
            this.rtbMessage.Size = new System.Drawing.Size(508, 94);
            this.rtbMessage.TabIndex = 16;
            this.rtbMessage.Text = "";
            // 
            // pageGet
            // 
            this.pageGet.Controls.Add(this.bSaveAttachment);
            this.pageGet.Controls.Add(this.splitContainer1);
            this.pageGet.Controls.Add(this.rtbPop3Status);
            this.pageGet.Controls.Add(this.cbPop3SSL);
            this.pageGet.Controls.Add(this.label1);
            this.pageGet.Controls.Add(this.tbPop3Pass);
            this.pageGet.Controls.Add(this.label2);
            this.pageGet.Controls.Add(this.tbPop3Login);
            this.pageGet.Controls.Add(this.label3);
            this.pageGet.Controls.Add(this.tbPop3Port);
            this.pageGet.Controls.Add(this.label4);
            this.pageGet.Controls.Add(this.tbPop3Server);
            this.pageGet.Controls.Add(this.bGetMail);
            this.pageGet.Location = new System.Drawing.Point(4, 22);
            this.pageGet.Name = "pageGet";
            this.pageGet.Padding = new System.Windows.Forms.Padding(3);
            this.pageGet.Size = new System.Drawing.Size(522, 336);
            this.pageGet.TabIndex = 1;
            this.pageGet.Text = "Получение";
            this.pageGet.UseVisualStyleBackColor = true;
            // 
            // rtbPop3Status
            // 
            this.rtbPop3Status.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbPop3Status.Location = new System.Drawing.Point(3, 275);
            this.rtbPop3Status.Name = "rtbPop3Status";
            this.rtbPop3Status.ReadOnly = true;
            this.rtbPop3Status.Size = new System.Drawing.Size(373, 55);
            this.rtbPop3Status.TabIndex = 42;
            this.rtbPop3Status.Text = "";
            // 
            // cbPop3SSL
            // 
            this.cbPop3SSL.AutoSize = true;
            this.cbPop3SSL.Checked = true;
            this.cbPop3SSL.CheckState = System.Windows.Forms.CheckState.Checked;
            this.cbPop3SSL.Location = new System.Drawing.Point(421, 8);
            this.cbPop3SSL.Name = "cbPop3SSL";
            this.cbPop3SSL.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
            this.cbPop3SSL.Size = new System.Drawing.Size(46, 17);
            this.cbPop3SSL.TabIndex = 40;
            this.cbPop3SSL.Text = "SSL";
            this.cbPop3SSL.UseVisualStyleBackColor = true;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(231, 35);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(45, 13);
            this.label1.TabIndex = 39;
            this.label1.Text = "Пароль";
            // 
            // tbPop3Pass
            // 
            this.tbPop3Pass.Location = new System.Drawing.Point(282, 32);
            this.tbPop3Pass.Name = "tbPop3Pass";
            this.tbPop3Pass.Size = new System.Drawing.Size(100, 20);
            this.tbPop3Pass.TabIndex = 38;
            this.tbPop3Pass.Text = "n9HXu0U3bPwZZLD67z9Z";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(7, 35);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(38, 13);
            this.label2.TabIndex = 37;
            this.label2.Text = "Логин";
            // 
            // tbPop3Login
            // 
            this.tbPop3Login.Location = new System.Drawing.Point(102, 32);
            this.tbPop3Login.Name = "tbPop3Login";
            this.tbPop3Login.Size = new System.Drawing.Size(100, 20);
            this.tbPop3Login.TabIndex = 36;
            this.tbPop3Login.Text = "ed_test";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(231, 9);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(32, 13);
            this.label3.TabIndex = 35;
            this.label3.Text = "Порт";
            // 
            // tbPop3Port
            // 
            this.tbPop3Port.Location = new System.Drawing.Point(282, 6);
            this.tbPop3Port.Name = "tbPop3Port";
            this.tbPop3Port.Size = new System.Drawing.Size(100, 20);
            this.tbPop3Port.TabIndex = 34;
            this.tbPop3Port.Text = "995";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(7, 9);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(89, 13);
            this.label4.TabIndex = 33;
            this.label4.Text = "Адресе сервера";
            // 
            // tbPop3Server
            // 
            this.tbPop3Server.Location = new System.Drawing.Point(102, 6);
            this.tbPop3Server.Name = "tbPop3Server";
            this.tbPop3Server.Size = new System.Drawing.Size(100, 20);
            this.tbPop3Server.TabIndex = 32;
            this.tbPop3Server.Text = "post.tbc.su";
            // 
            // bGetMail
            // 
            this.bGetMail.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.bGetMail.Location = new System.Drawing.Point(382, 274);
            this.bGetMail.Name = "bGetMail";
            this.bGetMail.Size = new System.Drawing.Size(137, 55);
            this.bGetMail.TabIndex = 21;
            this.bGetMail.Text = "Получить";
            this.bGetMail.UseVisualStyleBackColor = true;
            this.bGetMail.Click += new System.EventHandler(this.bGetMail_Click);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.splitContainer1.Location = new System.Drawing.Point(6, 58);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.dgvMessages);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.rtbPop3Message);
            this.splitContainer1.Size = new System.Drawing.Size(510, 210);
            this.splitContainer1.SplitterDistance = 221;
            this.splitContainer1.TabIndex = 43;
            // 
            // rtbPop3Message
            // 
            this.rtbPop3Message.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbPop3Message.Location = new System.Drawing.Point(0, 0);
            this.rtbPop3Message.Name = "rtbPop3Message";
            this.rtbPop3Message.Size = new System.Drawing.Size(285, 210);
            this.rtbPop3Message.TabIndex = 45;
            this.rtbPop3Message.Text = "";
            // 
            // dgvMessages
            // 
            this.dgvMessages.AllowUserToAddRows = false;
            this.dgvMessages.AllowUserToDeleteRows = false;
            this.dgvMessages.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgvMessages.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvMessages.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvMessages.Location = new System.Drawing.Point(0, 0);
            this.dgvMessages.MultiSelect = false;
            this.dgvMessages.Name = "dgvMessages";
            this.dgvMessages.ReadOnly = true;
            this.dgvMessages.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvMessages.Size = new System.Drawing.Size(221, 210);
            this.dgvMessages.TabIndex = 44;
            this.dgvMessages.SelectionChanged += new System.EventHandler(this.dgvMessages_SelectionChanged);
            // 
            // bSaveAttachment
            // 
            this.bSaveAttachment.Location = new System.Drawing.Point(388, 29);
            this.bSaveAttachment.Name = "bSaveAttachment";
            this.bSaveAttachment.Size = new System.Drawing.Size(126, 23);
            this.bSaveAttachment.TabIndex = 44;
            this.bSaveAttachment.Text = "Сохранить вложение";
            this.bSaveAttachment.UseVisualStyleBackColor = true;
            this.bSaveAttachment.Visible = false;
            this.bSaveAttachment.Click += new System.EventHandler(this.bSaveAttachment_Click);
            // 
            // MailTestForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(530, 362);
            this.Controls.Add(this.tabControl1);
            this.Name = "MailTestForm";
            this.Text = "Тестирование почтового ящика";
            this.Load += new System.EventHandler(this.MailTestForm_Load);
            this.tabControl1.ResumeLayout(false);
            this.pageSend.ResumeLayout(false);
            this.pageSend.PerformLayout();
            this.pageGet.ResumeLayout(false);
            this.pageGet.PerformLayout();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvMessages)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.OpenFileDialog openAttachmentDialog;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage pageSend;
        private System.Windows.Forms.Button bSend;
        private System.Windows.Forms.RichTextBox rtbStatus;
        private System.Windows.Forms.Button bAttachment;
        private System.Windows.Forms.TextBox tbAttachment;
        private System.Windows.Forms.RichTextBox rtbMessage;
        private System.Windows.Forms.TabPage pageGet;
        private System.Windows.Forms.Label lSubject;
        private System.Windows.Forms.TextBox tbSubject;
        private System.Windows.Forms.Button bGetMail;
        private System.Windows.Forms.Label lSender;
        private System.Windows.Forms.TextBox tbSender;
        private System.Windows.Forms.Label lRecipient;
        private System.Windows.Forms.TextBox tbRecipient;
        private System.Windows.Forms.CheckBox cbSSL;
        private System.Windows.Forms.Label lPassword;
        private System.Windows.Forms.TextBox tbPassword;
        private System.Windows.Forms.Label lLogin;
        private System.Windows.Forms.TextBox tbLogin;
        private System.Windows.Forms.Label lPort;
        private System.Windows.Forms.TextBox tbPort;
        private System.Windows.Forms.Label lServer;
        private System.Windows.Forms.TextBox tbServer;
        private System.Windows.Forms.RichTextBox rtbPop3Status;
        private System.Windows.Forms.CheckBox cbPop3SSL;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox tbPop3Pass;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.TextBox tbPop3Login;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox tbPop3Port;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox tbPop3Server;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.DataGridView dgvMessages;
        private System.Windows.Forms.RichTextBox rtbPop3Message;
        private System.Windows.Forms.Button bSaveAttachment;
        private System.Windows.Forms.FolderBrowserDialog selectAttachmentFolder;
    }
}

