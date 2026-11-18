using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MailKit.Security;
using mailTester.Mail;
using mailTester.Monitoring;

namespace mailTester;

internal sealed class MonitorProfileForm : Form
{
    private readonly MonitorJob original;
    private readonly FlowLayoutPanel fields;
    private readonly List<(Control Row, Func<MonitorKind,bool> Visible)> rows=new();
    private readonly ComboBox kind, tlsMode, mailProtocol, security, receiveProtocol, receiveSecurity;
    private readonly TextBox name,target,resolver,selectors,login,password,receiveHost,receiveLogin,receivePassword,from,to;
    private readonly NumericUpDown interval,port,receivePort,wait,poll;
    private readonly CheckBox enabled;
    private readonly Label hint;
    public MonitorJob Result {get;private set;}

    public MonitorProfileForm(MonitorJob job)
    {
        original=job;
        var j=job??new MonitorJob();
        Text=job==null?"Новый профиль мониторинга":"Настройка профиля";
        StartPosition=FormStartPosition.CenterParent;Size=new Size(700,740);MinimumSize=new Size(610,530);Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12)};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        Controls.Add(root);hint=new Label{Dock=DockStyle.Fill,ForeColor=Color.FromArgb(71,85,105),TextAlign=ContentAlignment.MiddleLeft};
        root.Controls.Add(hint,0,0);
        fields=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};
        root.Controls.Add(fields,0,1);
        bool Any(MonitorKind k)=>true;
        bool Mail(MonitorKind k)=>k is MonitorKind.Mail or MonitorKind.RoundTrip;
        bool Trip(MonitorKind k)=>k==MonitorKind.RoundTrip;
        bool Port(MonitorKind k)=>k is MonitorKind.Tcp or MonitorKind.Tls or MonitorKind.Mail or MonitorKind.RoundTrip;
        kind=EnumBox(j.Kind);name=Edit(j.Name);target=Edit(j.Target);interval=Num(j.IntervalMinutes,1,10080);
        enabled=new CheckBox{Checked=j.Enabled,Text="Разрешить проверки по расписанию",AutoSize=true};
        Add("Тип проверки",kind,Any);Add("Название",name,Any);Add("Цель (домен / IP / URL / SMTP)",target,Any);Add("Интервал, минут",interval,Any);Add("Активен",enabled,Any);
        resolver=Edit(j.Resolver);selectors=Edit(j.Selectors);
        Add("DNS-сервер, IP (необяз.)",resolver,k=>k is MonitorKind.Dns or MonitorKind.Policy or MonitorKind.Dnsbl);
        Add("DKIM-селекторы",selectors,k=>k==MonitorKind.Policy);
        tlsMode=EnumBox(j.TlsMode);mailProtocol=EnumBox(j.MailProtocol);port=Num(j.Port,1,65535);
        Add("Режим TLS",tlsMode,k=>k==MonitorKind.Tls);Add("Почтовый протокол",mailProtocol,k=>k==MonitorKind.Mail);Add("Порт",port,Port);
        security=SecurityBox(j.Security);login=Edit(j.Login);password=Secret(j.PasswordCipher);
        Add("TLS (исходящий / сервер)",security,Mail);Add("Логин (если нужен AUTH)",login,Mail);Add("Пароль (DPAPI)",password,Mail);
        from=Edit(j.From);to=Edit(j.To);receiveHost=Edit(j.ReceiveHost);receivePort=Num(j.ReceivePort,1,65535);receiveProtocol=EnumBox(j.ReceiveProtocol);
        receiveSecurity=SecurityBox(j.ReceiveSecurity);receiveLogin=Edit(j.ReceiveLogin);receivePassword=Secret(j.ReceivePasswordCipher);
        wait=Num(j.WaitSeconds,15,180);poll=Num(j.PollSeconds,5,30);
        Add("Отправитель (тестовый)",from,Trip);Add("Получатель (тестовый)",to,Trip);Add("Сервер получения",receiveHost,Trip);
        Add("Протокол получения",receiveProtocol,Trip);Add("Порт получения",receivePort,Trip);Add("TLS получения",receiveSecurity,Trip);
        Add("Логин получения",receiveLogin,Trip);Add("Пароль получения (DPAPI)",receivePassword,Trip);Add("Ожидание, секунд",wait,Trip);Add("Опрос, секунд",poll,Trip);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        var save=new Button{Text="Сохранить",Width=120,Height=34};var cancel=new Button{Text="Отмена",Width=95,Height=34,DialogResult=DialogResult.Cancel};
        buttons.Controls.Add(save);buttons.Controls.Add(cancel);root.Controls.Add(buttons,0,2);CancelButton=cancel;save.Click+=(_,_)=>SaveProfile();
        kind.SelectedIndexChanged+=(_,_)=>{interval.Value=MonitorJob.DefaultInterval((MonitorKind)kind.SelectedItem);Defaults();ShowFields();};
        tlsMode.SelectedIndexChanged+=(_,_)=>{if((MonitorKind)kind.SelectedItem==MonitorKind.Tls)port.Value=TlsDiagnostics.DefaultPort((TlsMode)tlsMode.SelectedItem);};
        mailProtocol.SelectedIndexChanged+=(_,_)=>Defaults();security.SelectedIndexChanged+=(_,_)=>Defaults();
        receiveProtocol.SelectedIndexChanged+=(_,_)=>ReceiveDefaults();receiveSecurity.SelectedIndexChanged+=(_,_)=>ReceiveDefaults();
        fields.Resize+=(_,_)=>ResizeRows();
        ShowFields();ResizeRows();
    }

    private static TextBox Edit(string value)=>new(){Text=value??"",Dock=DockStyle.Fill};
    private static TextBox Secret(string cipher)=>new(){Dock=DockStyle.Fill,UseSystemPasswordChar=true,PlaceholderText=string.IsNullOrEmpty(cipher)?"Пароль":"Сохранён; пусто = оставить при неизменном сервере"};
    private static NumericUpDown Num(int value,int min,int max)=>new(){Minimum=min,Maximum=max,Value=Math.Clamp(value,min,max),Dock=DockStyle.Fill};
    private static ComboBox EnumBox<T>(T value)where T:struct,Enum{
        var box=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
        box.Items.AddRange(Enum.GetValues<T>().Cast<object>().ToArray());box.SelectedItem=value;if(box.SelectedIndex<0)box.SelectedIndex=0;return box;
    }
    private static ComboBox SecurityBox(SecureSocketOptions value){
        var box=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
        box.Items.AddRange(new object[]{SecureSocketOptions.StartTls,SecureSocketOptions.SslOnConnect});box.SelectedItem=value;if(box.SelectedIndex<0)box.SelectedIndex=0;return box;
    }
    private void Add(string text,Control control,Func<MonitorKind,bool> visible){
        var row=new TableLayoutPanel{Width=590,Height=34,ColumnCount=2,RowCount=1,Margin=new Padding(0,1,0,1)};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        row.Controls.Add(new Label{Text=text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);row.Controls.Add(control,1,0);
        fields.Controls.Add(row);rows.Add((row,visible));
    }
    private void ResizeRows(){foreach(var r in rows)r.Row.Width=Math.Max(400,fields.ClientSize.Width-25);}
    private void Defaults(){
        var k=(MonitorKind)kind.SelectedItem;
        var s=(SecureSocketOptions)security.SelectedItem;
        bool implicitTls=s==SecureSocketOptions.SslOnConnect;
        if(k==MonitorKind.Tls)port.Value=TlsDiagnostics.DefaultPort((TlsMode)tlsMode.SelectedItem);
        else if(k==MonitorKind.RoundTrip)port.Value=implicitTls?465:587;
        else if(k==MonitorKind.Mail)port.Value=(MailProbeProtocol)mailProtocol.SelectedItem switch {MailProbeProtocol.Smtp=>implicitTls?465:587,MailProbeProtocol.Imap=>implicitTls?993:143,_=>implicitTls?995:110};
    }
    private void ReceiveDefaults(){
        bool ssl=(SecureSocketOptions)receiveSecurity.SelectedItem==SecureSocketOptions.SslOnConnect;
        receivePort.Value=(ReceiveProtocol)receiveProtocol.SelectedItem==ReceiveProtocol.Imap?(ssl?993:143):(ssl?995:110);
    }
    private void ShowFields(){
        var k=(MonitorKind)kind.SelectedItem;
        foreach(var row in rows)row.Row.Visible=row.Visible(k);
        hint.Text=k switch {
            MonitorKind.Http=>"GET URL без редиректов; 2xx = OK, 3xx = предупреждение.",
            MonitorKind.RoundTrip=>"Реальные тестовые письма: выделенный ящик. Интервал ≥ 15 минут. Письма не удаляются автоматически.",
            MonitorKind.Mail=>"TLS и почтовый протокол, AUTH при указанном логине. Писем не отправляет.",
            MonitorKind.Dnsbl=>"IP передаётся Spamhaus/SpamCop. Возможен UNKNOWN при ограничениях DNS.",
            MonitorKind.Policy=>"Проверяет SPF/DKIM/DMARC записи. Для DKIM нужны селекторы.",
            MonitorKind.Registration=>"Введите зарегистрированный домен; предупреждение за 30 дней.",
            _=>"Проверка по сохранённому профилю. Не требует Telegram или облака."
        };
    }

    private string PasswordCipher(TextBox input,string user,string previous,bool same){
        if(string.IsNullOrWhiteSpace(user))return "";
        if(input.TextLength>0)return SecretStore.Protect(input.Text);
        if(same&&!string.IsNullOrEmpty(previous))return previous;
        throw new ArgumentException("Введите пароль для указанного логина.");
    }
    private void SaveProfile(){
        try{
            var k=(MonitorKind)kind.SelectedItem;
            var j=new MonitorJob{
                Id=original?.Id??Guid.NewGuid().ToString("N"),Name=name.Text.Trim(),Kind=k,Target=target.Text.Trim(),IntervalMinutes=(int)interval.Value,Enabled=enabled.Checked,
                Port=(int)port.Value,Resolver=resolver.Text.Trim(),Selectors=selectors.Text.Trim(),TlsMode=(TlsMode)tlsMode.SelectedItem,MailProtocol=(MailProbeProtocol)mailProtocol.SelectedItem,
                Security=(SecureSocketOptions)security.SelectedItem,ReceiveHost=receiveHost.Text.Trim(),ReceivePort=(int)receivePort.Value,
                ReceiveProtocol=(ReceiveProtocol)receiveProtocol.SelectedItem,ReceiveSecurity=(SecureSocketOptions)receiveSecurity.SelectedItem,
                From=from.Text.Trim(),To=to.Text.Trim(),WaitSeconds=(int)wait.Value,PollSeconds=(int)poll.Value
            };
            if(k is MonitorKind.Mail or MonitorKind.RoundTrip){
                j.Login=login.Text.Trim();
                bool same=original!=null&&original.Kind==k&&original.Target==j.Target&&original.Port==j.Port&&original.Login==j.Login&&original.Security==j.Security&&original.MailProtocol==j.MailProtocol;
                j.PasswordCipher=PasswordCipher(password,j.Login,original?.PasswordCipher,same);
            }
            if(k==MonitorKind.RoundTrip){
                j.ReceiveLogin=receiveLogin.Text.Trim();
                bool same=original!=null&&original.Kind==k&&original.ReceiveHost==j.ReceiveHost&&original.ReceivePort==j.ReceivePort&&original.ReceiveLogin==j.ReceiveLogin&&original.ReceiveSecurity==j.ReceiveSecurity&&original.ReceiveProtocol==j.ReceiveProtocol;
                j.ReceivePasswordCipher=PasswordCipher(receivePassword,j.ReceiveLogin,original?.ReceivePasswordCipher,same);
            }
            j.Validate();Result=j;password.Clear();receivePassword.Clear();DialogResult=DialogResult.OK;Close();
        }catch(ArgumentException ex){MessageBox.Show(this,ex.Message,"Профиль",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        catch(Exception ex){MessageBox.Show(this,"Не удалось проверить профиль: "+ex.GetType().Name);}
    }
}
