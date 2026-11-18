using System.Diagnostics;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Pop3;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace mailTester.Mail
{
    public enum TripState { Delivered, NotObserved, SendUnknown, SendFailed, ReceiveFailed }
    public sealed record RoundTripResult(TripState State, string CorrelationId, double ElapsedSeconds, double? DeliverySeconds, string Summary);

    public static partial class MailEngine
    {
        public static bool IsTripHeader(HeaderList headers, string token) =>
            headers.Any(h => h.Field.Equals("X-MailTester-ID", StringComparison.OrdinalIgnoreCase) &&
                h.Value.Trim().Equals(token, StringComparison.Ordinal));

        public static void ValidateTrip(MailConnection smtp, MailConnection receiver, string from, string to, int waitSeconds, int pollSeconds)
        {
            smtp.Validate(false);
            receiver.Validate(true);
            if (smtp.Security == SecureSocketOptions.None || receiver.Security == SecureSocketOptions.None)
                throw new ArgumentException("Round Trip требует явный TLS с обеих сторон.");
            MailboxAddress.Parse(from);
            MailboxAddress.Parse(to);
            if (waitSeconds is < 15 or > 180 || pollSeconds is < 5 or > 30 || pollSeconds > waitSeconds)
                throw new ArgumentException("Ожидание 15–180 с; интервал 5–30 с и не больше ожидания.");
        }

        public static async Task<RoundTripResult> RoundTripAsync(MailConnection smtp, MailConnection receiver, ReceiveProtocol protocol,
            string from, string to, int waitSeconds, int pollSeconds, IProgress<string> progress, CancellationToken ct)
        {
            ValidateTrip(smtp, receiver, from, to, waitSeconds, pollSeconds);
            if (protocol is not (ReceiveProtocol.Imap or ReceiveProtocol.Pop3)) throw new ArgumentException("Протокол");
            string id = Guid.NewGuid().ToString("N");
            string subject = "MailTester probe " + id;
            var watch = Stopwatch.StartNew();
            RoundTripResult Finish(TripState state, string summary, double? delivery = null) => new(state, id, watch.Elapsed.TotalSeconds, delivery, summary);

            // Verify mailbox access before sending; avoid predictable orphaned probes on bad credentials.
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                await ProbeMailboxAsync(receiver, protocol, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { return Finish(TripState.ReceiveFailed, "Предварительная проверка ящика: " + ex.GetType().Name); }

            bool sending = false, accepted = false;
            var deliveryWatch = new Stopwatch();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                using var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(from)); message.To.Add(MailboxAddress.Parse(to));
                message.Subject = subject;
                message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
                message.Headers.Add("X-MailTester-ID", id);
                message.Body = new TextPart("plain") { Text = "MailTester delivery probe. ID: " + id };
                using var client = new SmtpClient();
                await ConnectAsync(client, smtp, progress, timeout.Token).ConfigureAwait(false);
                progress?.Report("Round Trip: отправка одного тестового письма");
                sending = true;
                deliveryWatch.Start();
                await client.SendAsync(message, timeout.Token).ConfigureAwait(false);
                accepted = true;
                progress?.Report("SMTP принял письмо; ожидаем поступление в тестовый ящик");
                await DisconnectAsync(client, null).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (!accepted)
            {
                return Finish(sending ? TripState.SendUnknown : TripState.SendFailed,
                    (sending ? "Статус принятия не подтверждён; повторной отправки нет: " : "SMTP не подключился: ") + ex.GetType().Name);
            }

            bool checkedMailbox = false;
            // Separate bounded receive window; no re-send, even following transient read failures.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(waitSeconds));
            try
            {
                while (!deadline.IsCancellationRequested)
                {
                    try
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                        attempt.CancelAfter(TimeSpan.FromSeconds(20));
                        bool found = await FindTripAsync(receiver, protocol, id, subject, attempt.Token).ConfigureAwait(false);
                        checkedMailbox = true;
                        if (found) return Finish(TripState.Delivered, "Уникальное письмо найдено", deliveryWatch.Elapsed.TotalSeconds);
                    }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested) { break; }
                    catch (Exception ex) { progress?.Report("Проверка ящика: " + ex.GetType().Name); }
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), deadline.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            ct.ThrowIfCancellationRequested();
            return Finish(checkedMailbox ? TripState.NotObserved : TripState.ReceiveFailed,
                checkedMailbox ? "SMTP принял письмо, но в пределах окна оно не найдено (не доказательство потери)." :
                "SMTP принял письмо, но получение не удалось проверить.");
        }

        private static async Task ProbeMailboxAsync(MailConnection connection, ReceiveProtocol protocol, CancellationToken ct)
        {
            using MailService client = protocol == ReceiveProtocol.Imap ? new ImapClient() : new Pop3Client();
            await ConnectAsync(client, connection, null, ct).ConfigureAwait(false);
            if (client is ImapClient imap) await imap.Inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
            await client.DisconnectAsync(false, ct).ConfigureAwait(false);
        }

        private static async Task<bool> FindTripAsync(MailConnection connection, ReceiveProtocol protocol, string id, string subject, CancellationToken ct)
        {
            if (protocol == ReceiveProtocol.Imap)
            {
                using var client = new ImapClient();
                await ConnectAsync(client, connection, null, ct).ConfigureAwait(false);
                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, ct).ConfigureAwait(false);
                var ids = await inbox.SearchAsync(SearchQuery.SubjectContains(subject), ct).ConfigureAwait(false);
                foreach (var uid in ids.Take(30))
                    if (IsTripHeader(await inbox.GetHeadersAsync(uid, ct).ConfigureAwait(false), id)) return true;
                return false;
            }
            using (var client = new Pop3Client())
            {
                await ConnectAsync(client, connection, null, ct).ConfigureAwait(false);
                // POP3 has no server search. Check headers of at most 100 newest messages (TOP).
                int count = client.Count;
                for (int i = count - 1; i >= Math.Max(0, count - 100); i--)
                    if (IsTripHeader(await client.GetMessageHeadersAsync(i, ct).ConfigureAwait(false), id)) return true;
                return false;
            }
        }
    }
}
