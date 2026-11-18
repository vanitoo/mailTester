using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Security;

namespace mailTester.Mail
{
    public enum TlsMode { Https, SmtpStartTls, SmtpImplicit, Pop3StartTls, Pop3Implicit, ImapStartTls, ImapImplicit }
    public static class TlsDiagnostics
    {
        public static int DefaultPort(TlsMode mode) => mode switch {
            TlsMode.Https => 443, TlsMode.SmtpStartTls => 587, TlsMode.SmtpImplicit => 465,
            TlsMode.Pop3StartTls => 110, TlsMode.Pop3Implicit => 995,
            TlsMode.ImapStartTls => 143, TlsMode.ImapImplicit => 993, _ => throw new ArgumentException("TLS mode")
        };

        public static async Task<IReadOnlyList<CheckFinding>> InspectAsync(string host, int port, TlsMode mode, CancellationToken ct)
        {
            host = DnsPolicyChecks.NormalizeDomain(host);
            if (port < 1 || port > 65535) throw new ArgumentException("Некорректный порт.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var token = timeout.Token;
            X509Certificate2 cert = null;
            SslPolicyErrors errors = SslPolicyErrors.RemoteCertificateNotAvailable;
            string chain = "", negotiated = "", failure = null;
            bool internalCaHint = false;
            bool Capture(object sender, X509Certificate certificate, X509Chain path, SslPolicyErrors validation)
            {
                cert?.Dispose();
                cert = certificate == null ? null : X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
                errors = validation;
                internalCaHint = cert != null && (cert.Issuer.Contains("DC=", StringComparison.OrdinalIgnoreCase) ||
                    cert.Issuer.Contains(".local", StringComparison.OrdinalIgnoreCase));
                chain = path == null ? "(нет цепочки)" : string.Join("\n", path.ChainElements.Cast<X509ChainElement>().Select(e =>
                    e.Certificate.Subject + " | " + string.Join(", ", e.ChainElementStatus.Select(s => s.Status.ToString()))));
                return validation == SslPolicyErrors.None; // NEVER accept invalid certificates.
            }
            try
            {
                if (mode == TlsMode.Https)
                {
                    using var tcp = new TcpClient();
                    await tcp.ConnectAsync(host, port, token).ConfigureAwait(false);
                    using var ssl = new SslStream(tcp.GetStream(), false, Capture);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                        TargetHost = host, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.Online
                    }, token).ConfigureAwait(false);
                    negotiated = ssl.SslProtocol + " / " + ssl.NegotiatedCipherSuite;
                }
                else
                {
                    using MailService client = mode switch {
                        TlsMode.SmtpStartTls or TlsMode.SmtpImplicit => new MailKit.Net.Smtp.SmtpClient(),
                        TlsMode.Pop3StartTls or TlsMode.Pop3Implicit => new MailKit.Net.Pop3.Pop3Client(),
                        _ => new MailKit.Net.Imap.ImapClient()
                    };
                    client.Timeout = 15000;
                    client.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                    client.CheckCertificateRevocation = true;
                    client.ServerCertificateValidationCallback = Capture;
                    var security = mode is TlsMode.SmtpStartTls or TlsMode.Pop3StartTls or TlsMode.ImapStartTls
                        ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
                    await client.ConnectAsync(host, port, security, token).ConfigureAwait(false);
                    negotiated = client.SslProtocol.ToString();
                    // Inspection only: no AUTH, no messages, no relay.
                    await client.DisconnectAsync(false, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { cert?.Dispose(); throw; }
            catch (Exception ex) { failure = ex.GetType().Name; }
            using (cert)
            {
                string endpoint = $"{host}:{port} ({mode})";
                if (cert == null) return new[] { new CheckFinding("TLS", endpoint, CheckLevel.Error, "Сертификат не получен", failure ?? "Нет данных") };
                var days = (cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;
                bool valid = failure == null && errors == SslPolicyErrors.None && cert.NotBefore.ToUniversalTime() <= DateTime.UtcNow && days >= 0;
                string san = string.Join("\n", cert.Extensions.Cast<X509Extension>().Where(x => x.Oid?.Value == "2.5.29.17").Select(x => x.Format(true)));
                string detail = $"Subject: {cert.Subject}\nIssuer: {cert.Issuer}\nSAN: {san}\nNotBefore: {cert.NotBefore:O}\nNotAfter: {cert.NotAfter:O}\nДней до окончания: {Math.Floor(days)}\nSHA-256: {cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)}\nTLS: {negotiated}\nValidation: {errors}\nHandshake: {failure ?? "OK"}\nЦепочка:\n{chain}\nПроверялись TLS 1.2/1.3 и системное доверие; не HTTP-содержимое, не полный аудит протоколов.";
                if (internalCaHint) detail += "\nПризнаки корпоративного CA (issuer DN). Доверие проверено только в текущем Windows trust store; внешнее доверие не подтверждено. Это эвристика, не полный аудит CA.";
                return new[] { new CheckFinding("TLS", endpoint, !valid ? CheckLevel.Error : days < 30 || internalCaHint ? CheckLevel.Warning : CheckLevel.Ok,
                    !valid ? "Сертификат/handshake не прошёл проверку" : $"Действует ещё {Math.Floor(days)} дн." + (internalCaHint ? " (возможен внутренний CA)" : ""), detail) };
            }
        }
    }
}
