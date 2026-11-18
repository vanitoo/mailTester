using System;
using System.IO;
using System.Net.Mail;
using Tbc.Common.Logging;
using System.Collections.Generic;

namespace Tbc.Common.Mail.Smtp
{

    /// <summary>
    /// Для использования данного класса необходимо 
    /// добавить в app.config:
    /// system.net mailSettings smtp from="" network host="" password="" userName="" /smtp  /mailSettings /system.net
    /// </summary>
    public class Smtp 
    {
        public static bool SendMessage(string from, string to, string subject, string body, Dictionary<string, byte[]> files)
        {
            List<Attachment> attachments = new List<Attachment>();
            if (files != null)
                foreach (var item in files)
                {
                    attachments.Add(new Attachment(new MemoryStream(item.Value), item.Key));
                }
            return SendMessage(from, to, subject, body, attachments);
        }

        public static bool SendMessage(string from, string to, string subject, string body, Attachment attachment)
        {
            return SendMessage(from, to, subject, body, new List<Attachment>() { attachment });
        }
        public static bool SendMessage(string from, string to, string subject, string body, List<Attachment> attachments)
        {
            MailMessage mailMessage = new MailMessage(from, to, subject, body);
            return SendMessage(mailMessage, attachments);
        }
        public static bool SendMessage(MailMessage mailMessage, List<Attachment> attachments) 
        {
            try
            {
                if (attachments != null)
                foreach (var item in attachments)
                {
                    mailMessage.Attachments.Add(item);
                }
                new SmtpClient().Send(mailMessage);
            }
            catch (Exception ex)
            {
                GlobalLoggin.CallError(ex);
                return false;
            }
            return true;
        }
    }
    //public class Smtp
    //{
    //    SmtpClient client;
    //    MailMessage message;
    //    readonly string from;
    //    string to;
    //    public Smtp() { message = new MailMessage(); }
    //    public Smtp(string host, int port)
    //    {
    //        client = new SmtpClient(host, port);
    //        message = new MailMessage();
    //    }

    //    public Smtp(string host, int port, string userName, string password, string domain)
    //        : this(host, port)
    //    {
    //        client.Credentials = new NetworkCredential(userName, password);
    //        from = userName + @"@" + domain;
    //    }
    //    public string From { get { return from; } }
    //    /// <summary>
    //    /// Адресс на который уходит копия сообщения отчёта о приёме
    //    /// </summary>
    //    public string To { set { to = value; } get { return to; } }
    //    public SmtpClient Client { set { client = value; } get { return client; } }
    //    public MailMessage Message { set { message = value; } get { return message; } }

    //    /// <summary>
    //    /// Метод отправки сообщения асинхронно одному адрессу
    //    /// </summary>
    //    /// <param name="from"></param>
    //    /// <param name="to"></param>
    //    /// <param name="body"></param>
    //    /// <param name="userToken"></param>
    //    /// <returns></returns>
    //    public bool SendMessage(string from, string to, string body, string subject, object userToken)
    //    {
    //        MailAddressCollection var = new MailAddressCollection();
    //        if (!string.IsNullOrEmpty(to))
    //            var.Add(new MailAddress(to));
    //        return SendMessage(from, var, body, subject, userToken);
    //    }

    //    public bool SendMessage(string body, string subject, object userToken)
    //    {
    //        if (SendMessage(from, to, body, subject, userToken)) { return true; }
    //        return false;
    //    }

    //    /// <summary>
    //    /// Метод отправки сообщения асинхронно большому количеству адрессов
    //    /// </summary>
    //    /// <param name="from"></param>
    //    /// <param name="to"></param>
    //    /// <param name="body"></param>
    //    /// <param name="userToken"></param>
    //    /// <returns></returns>
    //    public bool SendMessage(string from, MailAddressCollection to, string body, string subject, object userToken)
    //    {
    //        lock (this)
    //        {
    //            try
    //            {
    //                message.From = new MailAddress(from);
    //                message.To.Clear();
    //                foreach (MailAddress var in to)
    //                {
    //                    message.To.Add(var);
    //                }
    //                message.Body = body;
    //                message.Subject = subject;
    //                message.IsBodyHtml = true;
    //                message.BodyEncoding = Encoding.GetEncoding(20866);
    //                message.SubjectEncoding = Encoding.GetEncoding(20866);
    //                //Error
    //                client.Send(message);
    //                return true;
    //            }
    //            catch (Exception ex)
    //            {
    //                GlobalLoggin.CallError(ex);
    //                return false;
    //            }
    //        }
    //    }
    //}

}
