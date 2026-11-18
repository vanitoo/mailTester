using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Security;
using MailKit.Net.Smtp;
using MailKit.Net.Pop3;
using MailKit.Net.Imap;
using MimeKit;

namespace mailTester.Mail
{
    public enum ReceiveProtocol { Pop3, Imap }

    public record MailConnection(string Host, int Port, SecureSocketOptions Security, string Login, string Password)
    {
        // Do not include credentials in record diagnostics.
        public override string ToString() => $"{Host}:{Port} ({Security})";

        public void Validate(bool requireAuth)
        {
            if (string.IsNullOrWhiteSpace(Host) || Port < 1 || Port > 65535)
                throw new ArgumentException("Укажите сервер и порт от 1 до 65535.");
            if (Security != SecureSocketOptions.None && Security != SecureSocketOptions.StartTls &&
                Security != SecureSocketOptions.SslOnConnect)
                throw new ArgumentException("Выберите явный режим шифрования.");
            if (requireAuth || !string.IsNullOrEmpty(Login) || !string.IsNullOrEmpty(Password))
            {
                if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrEmpty(Password))
                    throw new ArgumentException("Введите логин и пароль.");
                if (Login.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                    Password.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new ArgumentException("Недопустимые символы в учётных данных.");
            }
        }
    }

    public sealed class MailDiagnosticException : Exception
    {
        public MailDiagnosticException(string stage, Exception error)
            : base(stage + ": " + error.GetType().Name +
                (error is SmtpCommandException smtp ? " (SMTP " + (int)smtp.StatusCode + ")" : ""))
        {
            // Server-controlled error text (including AUTH challenges) is never copied to logs.
        }
    }

    public static class MailEngine
    {
        private static void Configure(MailService client)
        {
            client.Timeout = 60000;
            client.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            // Default certificate validation stays enabled. No insecure fallback, no raw protocol logger.
        }

        private static async Task ConnectAsync(MailService client, MailConnection connection, IProgress<string> progress, CancellationToken ct)
        {
            Configure(client);
            progress?.Report("Подключение к серверу");
            try
            {
                await client.ConnectAsync(connection.Host.Trim(), connection.Port, connection.Security, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new MailDiagnosticException("Подключение/TLS", ex);
            }
            progress?.Report(client.IsSecure ? "TLS установлен" : "Соединение без шифрования");
            if (connection.Security != SecureSocketOptions.None && !client.IsSecure)
                throw new InvalidOperationException("Сервер не установил защищённое соединение.");
            if (!string.IsNullOrWhiteSpace(connection.Login))
            {
                progress?.Report("Авторизация");
                try
                {
                    client.AuthenticationMechanisms.Remove("XOAUTH2");
                    client.AuthenticationMechanisms.Remove("OAUTHBEARER");
                    await client.AuthenticateAsync(connection.Login, connection.Password, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw new MailDiagnosticException("Авторизация", ex);
                }
                progress?.Report("Авторизация успешна");
            }
        }

        private static async Task DisconnectAsync(MailService client, IProgress<string> progress)
        {
            if (!client.IsConnected) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await client.DisconnectAsync(true, timeout.Token).ConfigureAwait(false); }
            catch (Exception) { progress?.Report("Сессия закрыта без подтверждения QUIT/LOGOUT"); }
        }

        public static async Task SendAsync(MailConnection connection, string from, string to, string subject,
            string body, IReadOnlyList<string> attachments, IProgress<string> progress, CancellationToken ct)
        {
            connection.Validate(false);
            using var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.AddRange(InternetAddressList.Parse(to));
            if (message.To.Count == 0) throw new ArgumentException("Укажите получателя.");
            message.Subject = subject ?? "";
            var builder = new BodyBuilder { TextBody = body ?? "" };
            try
            {
                foreach (var path in attachments)
                {
                    ct.ThrowIfCancellationRequested();
                    // Local file loading is kept off the UI thread by the caller.
                    builder.Attachments.Add(path);
                }
                message.Body = builder.ToMessageBody();
            }
            catch
            {
                foreach (var attachment in builder.Attachments) attachment.Dispose();
                throw;
            }
            using var client = new SmtpClient();
            await ConnectAsync(client, connection, progress, ct).ConfigureAwait(false);
            progress?.Report("Передача письма SMTP");
            try
            {
                await client.SendAsync(message, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new MailDiagnosticException("Отправка SMTP (при обрыве связи статус доставки может быть неизвестен)", ex);
            }
            progress?.Report("SMTP-сервер принял письмо (это не подтверждение доставки получателю)");
            await DisconnectAsync(client, progress).ConfigureAwait(false);
        }

        public static async Task<List<ReceivedMessage>> ReceiveAsync(MailConnection connection, ReceiveProtocol protocol,
            int limit, IProgress<string> progress, CancellationToken ct)
        {
            connection.Validate(true);
            if (limit < 1 || limit > 200) throw new ArgumentOutOfRangeException(nameof(limit));
            if (protocol != ReceiveProtocol.Pop3 && protocol != ReceiveProtocol.Imap)
                throw new ArgumentOutOfRangeException(nameof(protocol));
            var messages = new List<ReceivedMessage>();
            try
            {
                if (protocol == ReceiveProtocol.Pop3)
                {
                    using var client = new Pop3Client();
                    await ConnectAsync(client, connection, progress, ct).ConfigureAwait(false);
                    await ReadLatestAsync(client.Count, limit, async index =>
                        await client.GetMessageAsync(index, ct).ConfigureAwait(false), messages, progress, ct).ConfigureAwait(false);
                    await DisconnectAsync(client, progress).ConfigureAwait(false);
                }
                else
                {
                    using var client = new ImapClient();
                    await ConnectAsync(client, connection, progress, ct).ConfigureAwait(false);
                    var inbox = client.Inbox;
                    await inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
                    await ReadLatestAsync(inbox.Count, limit, async index =>
                        await inbox.GetMessageAsync(index, ct).ConfigureAwait(false), messages, progress, ct).ConfigureAwait(false);
                    await DisconnectAsync(client, progress).ConfigureAwait(false);
                }
                return messages;
            }
            catch (Exception ex)
            {
                foreach (var message in messages) message.Dispose();
                if (ex is OperationCanceledException || ex is MailDiagnosticException) throw;
                throw new MailDiagnosticException("Получение " + protocol, ex);
            }
        }

        private static async Task ReadLatestAsync(int total, int limit, Func<int, Task<MimeMessage>> fetch,
            List<ReceivedMessage> messages, IProgress<string> progress, CancellationToken ct)
        {
            int count = Math.Min(total, limit);
            progress?.Report($"В ящике: {total}; загрузка: {count}");
            for (int index = total - 1; index >= total - count; index--)
            {
                ct.ThrowIfCancellationRequested();
                var message = await fetch(index).ConfigureAwait(false);
                try { messages.Add(new ReceivedMessage(message)); }
                catch { message.Dispose(); throw; }
                progress?.Report($"Получено {messages.Count}/{count}");
            }
        }
    }
}
