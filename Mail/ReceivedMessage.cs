using System;
using System.Linq;
using MimeKit;

namespace mailTester.Mail
{
    public sealed class ReceivedMessage : IDisposable
    {
        public MimeMessage Message { get; }
        public string From { get; }
        public string Subject { get; }
        public DateTime Date { get; }
        public string Body { get; }
        public bool HasAttachment { get; }

        public ReceivedMessage(MimeMessage message)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            From = message.From.ToString();
            Subject = message.Subject ?? "";
            Date = message.Date.LocalDateTime;
            Body = message.TextBody ?? (message.HtmlBody == null ? "" : "[HTML-код письма]\n" + message.HtmlBody);
            HasAttachment = message.Attachments.Any();
        }

        public void Dispose() => Message.Dispose();
    }
}
