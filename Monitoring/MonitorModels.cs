using System.Net;
using System.Security.Cryptography;
using System.Text;
using MailKit.Security;
using MimeKit;
using mailTester.Mail;

namespace mailTester.Monitoring;

public enum MonitorKind { Http, Tcp, Mail, Dns, Policy, Tls, Registration, Dnsbl, RoundTrip }
public enum MonitorStatus { Ok, Warning, Error, Unknown }
public enum MailProbeProtocol { Smtp, Pop3, Imap }

// Stored profile: plaintext credentials are never serialized. Only the password blob is DPAPI protected.
// Login, endpoint, address and result data remain local but unencrypted.
public sealed class MonitorJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public MonitorKind Kind { get; set; } = MonitorKind.Http;
    public string Target { get; set; } = "";
    public int IntervalMinutes { get; set; } = 5;
    public bool Enabled { get; set; }
    public int Port { get; set; } = 443;
    public string Resolver { get; set; } = "";
    public string Selectors { get; set; } = "";
    public TlsMode TlsMode { get; set; } = TlsMode.Https;
    public MailProbeProtocol MailProtocol { get; set; } = MailProbeProtocol.Smtp;
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;
    public string Login { get; set; } = "";
    public string PasswordCipher { get; set; } = "";
    public string ReceiveHost { get; set; } = "";
    public int ReceivePort { get; set; } = 993;
    public ReceiveProtocol ReceiveProtocol { get; set; } = ReceiveProtocol.Imap;
    public SecureSocketOptions ReceiveSecurity { get; set; } = SecureSocketOptions.SslOnConnect;
    public string ReceiveLogin { get; set; } = "";
    public string ReceivePasswordCipher { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public int WaitSeconds { get; set; } = 120;
    public int PollSeconds { get; set; } = 10;

    public override string ToString() => Name;

    public static int DefaultInterval(MonitorKind kind) => kind switch {
        MonitorKind.Http or MonitorKind.Tcp or MonitorKind.Mail => 5,
        MonitorKind.Dns => 60,
        MonitorKind.Policy or MonitorKind.Dnsbl => 360,
        MonitorKind.RoundTrip => 15,
        _ => 1440
    };

    public static bool ValidHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        if (IPAddress.TryParse(host, out _)) return true;
        try { DnsPolicyChecks.NormalizeDomain(host); return true; } catch (ArgumentException) { return false; }
    }

    public MailConnection Sender() => new(Target, Port, Security, Login, SecretStore.Unprotect(PasswordCipher));
    public MailConnection Receiver() => new(ReceiveHost, ReceivePort, ReceiveSecurity, ReceiveLogin, SecretStore.Unprotect(ReceivePasswordCipher));

    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 ||
            Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(Target) || Target.Length > 2048 ||
            Target.Any(char.IsControl) || IntervalMinutes is < 1 or > 10080 || !Enum.IsDefined(Kind))
            throw new ArgumentException("Имя, цель или интервал профиля некорректны (1–10080 мин).");
        if (Kind == MonitorKind.RoundTrip && IntervalMinutes < 15) throw new ArgumentException("Round Trip: интервал не менее 15 минут.");
        if (!string.IsNullOrWhiteSpace(Resolver) && !IPAddress.TryParse(Resolver, out _)) throw new ArgumentException("DNS resolver: нужен IP.");
        if (Kind == MonitorKind.Http)
        {
            if (!Uri.TryCreate(Target, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.UserInfo.Length > 0 || u.Fragment.Length > 0 || u.Query.Length > 0)
                throw new ArgumentException("Введите HTTP(S) URL без логина, query и fragment.");
        }
        else if (Kind == MonitorKind.Dnsbl)
        {
            if (!IPAddress.TryParse(Target, out _)) throw new ArgumentException("DNSBL: нужен IP.");
        }
        else if (Kind is MonitorKind.Dns or MonitorKind.Policy or MonitorKind.Registration or MonitorKind.Tls)
        {
            DnsPolicyChecks.NormalizeDomain(Target);
            if (Kind == MonitorKind.Policy) DnsPolicyChecks.ParseSelectors(Selectors);
            if (Kind == MonitorKind.Registration && !Target.Contains('.')) throw new ArgumentException("Введите зарегистрированный домен.");
            if (Kind == MonitorKind.Tls && !Enum.IsDefined(TlsMode)) throw new ArgumentException("TLS mode.");
        }
        else if (!ValidHost(Target)) throw new ArgumentException("Некорректное имя сервера.");
        if (Kind is MonitorKind.Tcp or MonitorKind.Tls or MonitorKind.Mail or MonitorKind.RoundTrip)
            if (Port is < 1 or > 65535) throw new ArgumentException("TCP-порт 1–65535.");
        if (Kind == MonitorKind.Mail)
        {
            if (!Enum.IsDefined(MailProtocol)) throw new ArgumentException("Mail protocol.");
            var sender = Sender();
            sender.Validate(false);
            if (sender.Security == SecureSocketOptions.None) throw new ArgumentException("Mail Monitor: обязателен TLS.");
        }
        if (Kind == MonitorKind.RoundTrip)
        {
            if (!ValidHost(ReceiveHost) || !Enum.IsDefined(ReceiveProtocol)) throw new ArgumentException("Получающий сервер/протокол.");
            MailEngine.ValidateTrip(Sender(), Receiver(), From, To, WaitSeconds, PollSeconds);
        }
    }
}

public static class SecretStore
{
    public static string Protect(string password)
    {
        if (string.IsNullOrEmpty(password)) return "";
        byte[] clear = Encoding.UTF8.GetBytes(password);
        try { return Convert.ToBase64String(ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public static string Unprotect(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return "";
        byte[] clear = ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
}

public sealed record MonitorRun(string JobId, string Name, DateTimeOffset Started, MonitorStatus Status,
    double Seconds, double? DeliverySeconds, string Summary, string Detail);
public sealed record MonitorEntry(MonitorJob Job, DateTimeOffset NextDue, bool Busy, MonitorRun Last);
public sealed record MonitorClaim(MonitorJob Job, string Token);
