using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace mailTester
{
    public partial class MailTestForm
    {
        private static readonly Color Canvas = Color.FromArgb(241, 245, 249);
        private static readonly Color Surface = Color.White;
        private static readonly Color Ink = Color.FromArgb(15, 23, 42);
        private static readonly Color Muted = Color.FromArgb(71, 85, 105);
        private static readonly Color Navy = Color.FromArgb(15, 23, 42);
        private static readonly Color Accent = Color.FromArgb(37, 99, 235);
        private static readonly Color Green = Color.FromArgb(21, 128, 61);
        private static readonly Color Red = Color.FromArgb(185, 28, 28);
        private static readonly Color BorderColor = Color.FromArgb(226, 232, 240);
        private static readonly Color Terminal = Color.FromArgb(15, 23, 42);

        private TextBox tbServer, tbPort, tbLogin, tbPassword, tbSender, tbRecipient, tbSubject, tbAttachment;
        private TextBox tbPop3Server, tbPop3Port, tbPop3Login, tbPop3Pass, searchMessages;
        private RichTextBox rtbMessage, rtbStatus, rtbPop3Status, rtbPop3Message, journalBox;
        private Button bSend, bGetMail, bSaveAttachment, bAttachment, bClearAttachments, bClearJournal, bCopyJournal, bExportJournal, bCancelGlobal;
        private CheckBox revealSmtp, revealReceive;
        private DataGridView dgvMessages;
        private Panel workspace, pageOverview, pageSend, pageGet, pageJournal, pageDns, pageTools, pageMonitor;
        private Label pageTitle, pageSubtitle, statusLabel, metricSent, metricReceived, metricErrors, metricLast, attachmentInfo, inboxCount;
        private Label previewSubject, previewMeta, previewFiles, sendState, receiveState;
        private readonly Dictionary<Panel, Button> navigation = new Dictionary<Panel, Button>();
        private readonly OpenFileDialog openAttachmentDialog = new OpenFileDialog { Multiselect = true, Title = "Выберите вложения" };
        private readonly FolderBrowserDialog selectAttachmentFolder = new FolderBrowserDialog { Description = "Куда сохранить вложения?" };

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            Font = new Font("Segoe UI", 10F);
            Text = "MailTester 1.6 | Mail diagnostics";
            BackColor = Canvas;
            ForeColor = Ink;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1200, 780);
            MinimumSize = new Size(1040, 700);

            var root = Table(2, 1, Canvas);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var sidebar = Table(1, 12, Navy);
            sidebar.Padding = new Padding(12, 20, 12, 14);
            sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 70, 32, 48, 48, 48, 48, 48, 48, 48 })
                sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.Controls.Add(sidebar, 0, 0);

            var brand = TextLabel("MailTester", 22, Color.White, true);
            brand.TextAlign = ContentAlignment.MiddleLeft;
            brand.Padding = new Padding(9, 0, 0, 0);
            sidebar.Controls.Add(brand, 0, 0);
            sidebar.Controls.Add(TextLabel("   РАБОЧАЯ ОБЛАСТЬ", 9, Color.FromArgb(148, 163, 184), true), 0, 1);

            var main = Table(1, 2, Canvas);
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(main, 1, 0);

            var header = Table(3, 2, Surface);
            header.Padding = new Padding(20, 10, 18, 9);
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
            pageTitle = TextLabel("Обзор", 20, Ink, true);
            pageSubtitle = TextLabel("Диагностика почтовых серверов", 10, Muted);
            statusLabel = TextLabel("●  Готово", 10, Green, true);
            statusLabel.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(pageTitle, 0, 0);
            header.Controls.Add(pageSubtitle, 0, 1);
            header.Controls.Add(statusLabel, 1, 0);
            header.SetRowSpan(statusLabel, 2);
            bCancelGlobal = MakeButton("Отмена", false);
            bCancelGlobal.Visible = false;
            bCancelGlobal.Margin = new Padding(12, 9, 0, 9);
            bCancelGlobal.Click += (_, _) => operation?.Cancel();
            header.Controls.Add(bCancelGlobal, 2, 0);
            header.SetRowSpan(bCancelGlobal, 2);
            main.Controls.Add(header, 0, 0);

            workspace = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(18), Margin = Padding.Empty };
            main.Controls.Add(workspace, 0, 1);

            pageOverview = NewPage();
            pageSend = NewPage();
            pageGet = NewPage();
            pageJournal = NewPage();
            pageDns = NewPage();
            pageTools = NewPage();
            pageMonitor = NewPage();

            BuildOverview();
            BuildSend();
            BuildReceive();
            BuildJournal();
            BuildDns();
            BuildTools();
            BuildMonitor();

            AddNav(sidebar, 2, "Обзор", pageOverview, "Обзор", "Диагностика и результаты текущей сессии");
            AddNav(sidebar, 3, "SMTP  ·  Отправка", pageSend, "SMTP / Отправка", "Проверка подключения, авторизации и отправки");
            AddNav(sidebar, 4, "POP3 / IMAP", pageGet, "POP3 / IMAP / Получение", "Проверка входящей почты и просмотр сообщений");
            AddNav(sidebar, 5, "DNS  ·  Домен", pageDns, "DNS / Домен", "DNS, MX, SPF, DKIM и DMARC");
            AddNav(sidebar, 6, "Инструменты", pageTools, "Расширенная диагностика", "SPF / EML / PTR / DNSBL / TLS / WHOIS / DNS");
            AddNav(sidebar, 7, "Мониторинг", pageMonitor, "Мониторинг", "Профили, состояние, Round Trip и история");
            AddNav(sidebar, 8, "Журнал", pageJournal, "Журнал диагностики", "События текущей сессии без паролей и сырого AUTH");
            sidebar.Controls.Add(TextLabel("   TLS 1.2 / 1.3", 10, Color.FromArgb(134, 239, 172)), 0, 10);
            sidebar.Controls.Add(TextLabel("   v1.6.0  •  .NET 10", 9, Color.FromArgb(148, 163, 184)), 0, 11);

            ShowPage(pageOverview, "Обзор", "Диагностика и результаты текущей сессии");
            ResumeLayout(true);
        }

        private Panel NewPage()
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Margin = Padding.Empty, Visible = false };
            workspace.Controls.Add(page);
            return page;
        }

        private void AddNav(TableLayoutPanel sidebar, int row, string caption, Panel page, string title, string subtitle)
        {
            var button = MakeButton(caption, false);
            button.BackColor = Navy;
            button.ForeColor = Color.FromArgb(203, 213, 225);
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(30, 41, 59);
            button.FlatAppearance.MouseDownBackColor = Accent;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(12, 0, 0, 0);
            button.Margin = new Padding(0, 3, 0, 3);
            button.Click += (_, _) => ShowPage(page, title, subtitle);
            sidebar.Controls.Add(button, 0, row);
            navigation.Add(page, button);
        }

        private void ShowPage(Panel page, string title, string subtitle)
        {
            foreach (var entry in navigation)
            {
                bool active = entry.Key == page;
                entry.Key.Visible = active;
                entry.Value.BackColor = active ? Accent : Navy;
                entry.Value.ForeColor = active ? Color.White : Color.FromArgb(203, 213, 225);
                entry.Value.FlatAppearance.MouseOverBackColor = active ? Accent : Color.FromArgb(30, 41, 59);
            }
            page.BringToFront();
            pageTitle.Text = title;
            pageSubtitle.Text = subtitle;
        }

        private void Navigate(Panel page) => navigation[page].PerformClick();

        private static TableLayoutPanel Table(int columns, int rows, Color background)
        {
            return new TableLayoutPanel {
                ColumnCount = columns, RowCount = rows, Dock = DockStyle.Fill, BackColor = background,
                Margin = Padding.Empty, Padding = Padding.Empty, GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
        }

        private static Label TextLabel(string text, float size, Color color, bool bold = false)
        {
            return new Label {
                Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = color, BackColor = Color.Transparent, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = Padding.Empty, AutoEllipsis = true
            };
        }

        private static TextBox Edit(string hint = "", string value = "", bool password = false)
        {
            return new TextBox {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = hint,
                Text = value, UseSystemPasswordChar = password, BackColor = Surface, ForeColor = Ink,
                Margin = new Padding(0, 3, 8, 4)
            };
        }

        private static Button MakeButton(string text, bool primary = true)
        {
            var button = new Button {
                Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
                BackColor = primary ? Accent : Surface, ForeColor = primary ? Color.White : Ink,
                Cursor = Cursors.Hand, Margin = new Padding(0, 3, 6, 3), TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 78, 216) : Canvas;
            return button;
        }

        private static RichTextBox TextArea(bool readOnly, bool terminal = false)
        {
            return new RichTextBox {
                Dock = DockStyle.Fill, ReadOnly = readOnly, BorderStyle = BorderStyle.None, DetectUrls = false,
                BackColor = terminal ? Terminal : Surface,
                ForeColor = terminal ? Color.FromArgb(203, 213, 225) : Ink,
                Font = new Font(terminal ? "Consolas" : "Segoe UI", terminal ? 10F : 11F),
                ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true, Margin = new Padding(1)
            };
        }

        private static TableLayoutPanel Card(int columns, int rows, Padding margin)
        {
            var table = Table(columns, rows, Surface);
            table.Padding = new Padding(12);
            table.Margin = margin;
            return table;
        }

        private static void AttachScrollablePage(Panel page, TableLayoutPanel layout, int minimumHeight)
        {
            page.AutoScroll = true;
            page.AutoScrollMinSize = new Size(0, minimumHeight);
            layout.MinimumSize = new Size(0, minimumHeight);
            page.Controls.Add(layout);
        }

        private static void Row(TableLayoutPanel table, float height) =>
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));

        private static void FillRow(TableLayoutPanel table) =>
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        private static void Col(TableLayoutPanel table, float width, bool relative = false) =>
            table.ColumnStyles.Add(new ColumnStyle(relative ? SizeType.Percent : SizeType.Absolute, width));

        private static void Span(TableLayoutPanel table, Control control, int col, int row, int span = 1)
        {
            table.Controls.Add(control, col, row);
            if (span > 1) table.SetColumnSpan(control, span);
        }

        private static void Field(TableLayoutPanel table, int col, int row, string title, Control control)
        {
            Span(table, TextLabel(title, 9, Muted, true), col, row);
            Span(table, control, col, row + 1);
        }

        private TableLayoutPanel Metric(string title, string value, out Label valueLabel)
        {
            var card = Card(1, 3, new Padding(0, 0, 10, 0));
            Col(card, 100, true);
            Row(card, 24);
            FillRow(card);
            Row(card, 20);
            Span(card, TextLabel(title, 10, Muted, true), 0, 0);
            valueLabel = TextLabel(value, 24, Ink, true);
            Span(card, valueLabel, 0, 1);
            Span(card, TextLabel("Текущая сессия", 9, Muted), 0, 2);
            return card;
        }

        private void BuildOverview()
        {
            var layout = Table(1, 4, Canvas);
            Col(layout, 100, true);
            Row(layout, 118);
            Row(layout, 16);
            Row(layout, 170);
            FillRow(layout);
            AttachScrollablePage(pageOverview, layout, 480);

            var metrics = Table(3, 1, Canvas);
            Col(metrics, 33, true); Col(metrics, 34, true); Col(metrics, 33, true);
            FillRow(metrics);
            Span(metrics, Metric("SMTP принято", "0", out metricSent), 0, 0);
            Span(metrics, Metric("Писем получено", "0", out metricReceived), 1, 0);
            var errors = Metric("Ошибки операций", "0", out metricErrors);
            errors.Margin = Padding.Empty;
            Span(metrics, errors, 2, 0);
            Span(layout, metrics, 0, 0);

            var quick = Card(2, 4, Padding.Empty);
            Col(quick, 50, true); Col(quick, 50, true);
            Row(quick, 32); Row(quick, 46); Row(quick, 18); FillRow(quick);
            Span(quick, TextLabel("Быстрый старт", 15, Ink, true), 0, 0, 2);
            var send = MakeButton("Открыть SMTP");
            var receive = MakeButton("Открыть POP3 / IMAP", false);
            send.Click += (_, _) => Navigate(pageSend);
            receive.Click += (_, _) => Navigate(pageGet);
            Span(quick, send, 0, 1); Span(quick, receive, 1, 1);
            Span(quick, TextLabel("Отправка, TLS и SMTP AUTH", 10, Muted), 0, 2);
            Span(quick, TextLabel("Inbox, сообщения и вложения", 10, Muted), 1, 2);
            metricLast = TextLabel("Последняя операция: ещё не выполнялась", 10, Muted);
            Span(quick, metricLast, 0, 3, 2);
            Span(layout, quick, 0, 2);

            var info = Card(1, 4, new Padding(0, 15, 0, 0));
            Col(info, 100, true);
            Row(info, 38); Row(info, 43); Row(info, 43); FillRow(info);
            Span(info, TextLabel("Почтовые инструменты", 16, Ink, true), 0, 0);
            Span(info, TextLabel("SMTP : 25 / 465 / 587      •      POP3 : 110 / 995      •      IMAP : 143 / 993", 11, Ink), 0, 1);
            Span(info, TextLabel("Сертификаты проверяются. IMAP открывает Inbox только для чтения.", 11, Muted), 0, 2);
            var openLog = MakeButton("Открыть журнал диагностики", false);
            openLog.Dock = DockStyle.Top;
            openLog.Height = 40;
            openLog.Click += (_, _) => Navigate(pageJournal);
            Span(info, openLog, 0, 3);
            Span(layout, info, 0, 3);
        }

        private void BuildSend()
        {
            var layout = Table(1, 3, Canvas);
            Col(layout, 100, true); Row(layout, 192); FillRow(layout); Row(layout, 154);
            AttachScrollablePage(pageSend, layout, 610);
            var connection = Card(3, 5, new Padding(0, 0, 0, 10));
            Col(connection, 54, true); Col(connection, 16, true); Col(connection, 30, true);
            Row(connection, 29); Row(connection, 21); Row(connection, 36); Row(connection, 21); FillRow(connection);
            Span(connection, TextLabel("01  /  Подключение", 14, Ink, true), 0, 0, 3);
            tbServer = Edit("smtp.example.com");
            tbPort = Edit("587", "587");
            tbLogin = Edit("Необязательно");
            tbPassword = Edit("Пароль приложения", password: true);
            Field(connection, 0, 1, "Сервер SMTP", tbServer);
            Field(connection, 1, 1, "Порт", tbPort);
            Field(connection, 2, 1, "Шифрование", smtpSecurity);
            Field(connection, 0, 3, "Логин", tbLogin);
            Field(connection, 1, 3, "Пароль", tbPassword);
            revealSmtp = new CheckBox { Text = "Показать пароль", Dock = DockStyle.Fill, ForeColor = Muted, Margin = new Padding(2) };
            revealSmtp.CheckedChanged += (_, _) => tbPassword.UseSystemPasswordChar = !revealSmtp.Checked;
            Span(connection, revealSmtp, 2, 4);
            Span(layout, connection, 0, 0);

            var compose = Card(2, 9, new Padding(0, 0, 0, 10));
            Col(compose, 50, true); Col(compose, 50, true);
            Row(compose, 28); Row(compose, 19); Row(compose, 32);
            Row(compose, 19); Row(compose, 32); Row(compose, 19); FillRow(compose); Row(compose, 19); Row(compose, 36);
            Span(compose, TextLabel("02  /  Тестовое письмо", 14, Ink, true), 0, 0, 2);
            tbSender = Edit("sender@example.com"); tbRecipient = Edit("recipient@example.com");
            Field(compose, 0, 1, "Отправитель", tbSender);
            Field(compose, 1, 1, "Получатель (несколько через запятую)", tbRecipient);
            tbSubject = Edit("Тема тестового сообщения", "MailTester test");
            Span(compose, TextLabel("Тема", 9, Muted, true), 0, 3, 2);
            Span(compose, tbSubject, 0, 4, 2);
            Span(compose, TextLabel("Текст письма (обычный текст)", 9, Muted, true), 0, 5, 2);
            rtbMessage = TextArea(false);
            rtbMessage.BorderStyle = BorderStyle.FixedSingle;
            rtbMessage.Text = "Тестовое сообщение MailTester.";
            Span(compose, rtbMessage, 0, 6, 2);
            attachmentInfo = TextLabel("Вложения: 0", 9, Muted, true);
            Span(compose, attachmentInfo, 0, 7, 2);
            var attachments = Table(3, 1, Surface);
            Col(attachments, 100, true); Col(attachments, 122); Col(attachments, 96); FillRow(attachments);
            tbAttachment = Edit("Нет вложений");
            tbAttachment.ReadOnly = true;
            bAttachment = MakeButton("Добавить", false);
            bClearAttachments = MakeButton("Очистить", false);
            bAttachment.Click += bAttachment_Click;
            bClearAttachments.Click += (_, _) => { ClearAttachments(); };
            Span(attachments, tbAttachment, 0, 0); Span(attachments, bAttachment, 1, 0); Span(attachments, bClearAttachments, 2, 0);
            Span(compose, attachments, 0, 8, 2);
            Span(layout, compose, 0, 1);

            var footer = Card(2, 2, Padding.Empty);
            Col(footer, 100, true); Col(footer, 165); Row(footer, 38); FillRow(footer);
            sendState = TextLabel("03  /  Диагностика SMTP", 13, Ink, true);
            bSend = MakeButton("Отправить");
            bSend.Click += bSend_Click;
            Span(footer, sendState, 0, 0); Span(footer, bSend, 1, 0);
            rtbStatus = TextArea(true, true);
            Span(footer, rtbStatus, 0, 1, 2);
            Span(layout, footer, 0, 2);
        }

        private void BuildReceive()
        {
            var layout = Table(1, 3, Canvas);
            Col(layout, 100, true); Row(layout, 192); FillRow(layout); Row(layout, 136);
            AttachScrollablePage(pageGet, layout, 530);

            var connection = Card(4, 5, new Padding(0, 0, 0, 10));
            Col(connection, 36, true); Col(connection, 16, true); Col(connection, 24, true); Col(connection, 24, true);
            Row(connection, 29); Row(connection, 21); Row(connection, 36); Row(connection, 21); FillRow(connection);
            Span(connection, TextLabel("01  /  Входящая почта", 14, Ink, true), 0, 0, 4);
            tbPop3Server = Edit("imap.example.com");
            tbPop3Port = Edit("995", "995");
            tbPop3Login = Edit("user@example.com");
            tbPop3Pass = Edit("Пароль приложения", password: true);
            Field(connection, 0, 1, "Сервер", tbPop3Server);
            Field(connection, 1, 1, "Порт", tbPop3Port);
            Field(connection, 2, 1, "Протокол", receiveProtocol);
            Field(connection, 3, 1, "Шифрование", receiveSecurity);
            Field(connection, 0, 3, "Логин", tbPop3Login);
            Field(connection, 1, 3, "Пароль", tbPop3Pass);
            Field(connection, 2, 3, "Последних писем", messageLimit);
            revealReceive = new CheckBox { Text = "Показать пароль", Dock = DockStyle.Fill, ForeColor = Muted, Margin = new Padding(2) };
            revealReceive.CheckedChanged += (_, _) => tbPop3Pass.UseSystemPasswordChar = !revealReceive.Checked;
            Span(connection, revealReceive, 3, 4);
            Span(layout, connection, 0, 0);

            var mail = Card(1, 2, new Padding(0, 0, 0, 10));
            Col(mail, 100, true); Row(mail, 42); FillRow(mail);
            var bar = Table(3, 1, Surface);
            Col(bar, 100, true); Col(bar, 250); Col(bar, 160); FillRow(bar);
            inboxCount = TextLabel("02  /  Письма · 0", 14, Ink, true);
            searchMessages = Edit("Поиск: тема, отправитель");
            searchMessages.TextChanged += (_, _) => ApplyFilter();
            bGetMail = MakeButton("Получить письма");
            bGetMail.Click += bGetMail_Click;
            Span(bar, inboxCount, 0, 0); Span(bar, searchMessages, 1, 0); Span(bar, bGetMail, 2, 0);
            Span(mail, bar, 0, 0);

            var panes = Table(2, 1, BorderColor);
            Col(panes, 47, true); Col(panes, 53, true); FillRow(panes);
            dgvMessages = new DataGridView {
                Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), BackgroundColor = Surface,
                BorderStyle = BorderStyle.None, GridColor = BorderColor, ReadOnly = true,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                AutoGenerateColumns = false, MultiSelect = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EnableHeadersVisualStyles = false, ColumnHeadersHeight = 32, RowTemplate = { Height = 34 }
            };
            dgvMessages.ColumnHeadersDefaultCellStyle.BackColor = Canvas;
            dgvMessages.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            dgvMessages.DefaultCellStyle.BackColor = Surface;
            dgvMessages.DefaultCellStyle.ForeColor = Ink;
            dgvMessages.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            dgvMessages.DefaultCellStyle.SelectionForeColor = Ink;
            dgvMessages.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "From", HeaderText = "От кого", FillWeight = 32, MinimumWidth = 65 });
            dgvMessages.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Subject", HeaderText = "Тема", FillWeight = 38, MinimumWidth = 65 });
            dgvMessages.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Date", HeaderText = "Дата", FillWeight = 30, MinimumWidth = 80, DefaultCellStyle = new DataGridViewCellStyle { Format = "dd.MM HH:mm" } });
            dgvMessages.SelectionChanged += dgvMessages_SelectionChanged;
            Span(panes, dgvMessages, 0, 0);

            var preview = Table(1, 4, Surface);
            preview.Padding = new Padding(8, 0, 0, 0);
            Col(preview, 100, true); Row(preview, 35); Row(preview, 40); FillRow(preview); Row(preview, 39);
            previewSubject = TextLabel("Выберите письмо", 14, Ink, true);
            previewMeta = TextLabel("Здесь будут данные сообщения", 9, Muted);
            rtbPop3Message = TextArea(true);
            Span(preview, previewSubject, 0, 0); Span(preview, previewMeta, 0, 1); Span(preview, rtbPop3Message, 0, 2);
            var save = Table(2, 1, Surface);
            Col(save, 100, true); Col(save, 156); FillRow(save);
            previewFiles = TextLabel("Вложения: —", 9, Muted);
            bSaveAttachment = MakeButton("Сохранить файлы", false);
            bSaveAttachment.Enabled = false;
            bSaveAttachment.Click += bSaveAttachment_Click;
            Span(save, previewFiles, 0, 0); Span(save, bSaveAttachment, 1, 0);
            Span(preview, save, 0, 3);
            Span(panes, preview, 1, 0);
            Span(mail, panes, 0, 1);
            Span(layout, mail, 0, 1);

            var footer = Card(1, 2, Padding.Empty);
            Col(footer, 100, true); Row(footer, 30); FillRow(footer);
            receiveState = TextLabel("03  /  Диагностика POP3 / IMAP", 13, Ink, true);
            rtbPop3Status = TextArea(true, true);
            Span(footer, receiveState, 0, 0); Span(footer, rtbPop3Status, 0, 1);
            Span(layout, footer, 0, 2);
        }

        private void BuildJournal()
        {
            var card = Card(1, 3, Padding.Empty);
            Col(card, 100, true); Row(card, 43); FillRow(card); Row(card, 32);
            pageJournal.Controls.Add(card);
            var toolbar = Table(4, 1, Surface);
            Col(toolbar, 100, true); Col(toolbar, 118); Col(toolbar, 118); Col(toolbar, 124); FillRow(toolbar);
            Span(toolbar, TextLabel("События", 15, Ink, true), 0, 0);
            bCopyJournal = MakeButton("Копировать", false);
            bExportJournal = MakeButton("Экспорт TXT");
            bClearJournal = MakeButton("Очистить", false);
            bCopyJournal.Click += (_, _) => CopyJournal();
            bExportJournal.Click += (_, _) => ExportJournal();
            bClearJournal.Click += (_, _) => { journalBox.Clear(); rtbStatus.Clear(); rtbPop3Status.Clear(); dnsLog.Clear(); toolsLog.Clear(); monitorLog.Clear(); };
            Span(toolbar, bCopyJournal, 1, 0); Span(toolbar, bExportJournal, 2, 0); Span(toolbar, bClearJournal, 3, 0);
            Span(card, toolbar, 0, 0);
            journalBox = TextArea(true, true);
            Span(card, journalBox, 0, 1);
            Span(card, TextLabel("Журнал хранится в памяти. Экспорт выполняется только по запросу.", 9, Muted), 0, 2);
        }
    }
}
