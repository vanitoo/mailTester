using System;
using System.IO;
using Tbc.Common.Mail.Pop3.Mime;
using Tbc.Common.Logging;

namespace Tbc.Common.Mail.Pop3
{
    public class Pop3
    {
        readonly EmailError emailError = new EmailError();
        readonly string serverAddress;
        readonly int port;
        readonly bool ssl;
        readonly string userName;
        readonly string pass;
        int numberOfMailsInMailbox, mailboxSize;

        public Pop3MimeClient Client { get; set; }

        public int MailboxSize
        {
            get { return mailboxSize; }
            set { mailboxSize = value; }
        }

        public int NumberOfMailsInMailbox
        {
            get { return numberOfMailsInMailbox; }
            set { numberOfMailsInMailbox = value; }
        }

        public Pop3(string serverAddress, int port, bool ssl, string userName, string pass) 
        { 
            this.serverAddress = serverAddress;
            this.port = port;
            this.ssl = ssl;
            this.userName = userName;
            this.pass = pass;
            Client = new Pop3MimeClient(serverAddress, port, ssl, userName, pass);
            Client.ReadTimeout = 60000;
            //client.Trace += Logging;
            emailError.Error += MailLogs;
        }

        /// <summary>
        /// Взять статистику по ящику
        /// </summary>
        /// <returns></returns>
        public bool GetMailboxStats()
        {
            //Берём информацию о статистике ящика(Количество Emails и размер ящика)
            if (!Client.GetMailboxStats(out numberOfMailsInMailbox, out mailboxSize))
            {
                //GlobalLoggin.CallError("client.GetMailboxStats(out numberOfMailsInMailbox, out mailboxSize)");
                TextfileLog.AddError("client.GetMailboxStats(out numberOfMailsInMailbox, out mailboxSize)");
                return false;
            }
            return true;
        }

        public bool DeleteMessage(int messageNumber)
        {
            try
            {
                Client.DeleteEmail(messageNumber); 
            }
            catch (Exception ex)
            {
                //GlobalLoggin.CallError(ex);
                TextfileLog.AddEntry(ex);
                return false;
            }
            finally
            {
                GC.Collect();
            }
            return true;
        }

        ///// <summary>
        ///// Приём почтового сообщения с сервера по номеру
        ///// </summary>
        ///// <param name="messageNumber"></param>
        ///// <param name="path"></param>
        ///// <returns></returns>
        //public bool RecieveMessage(int messageNumber, out string path)
        //{
        //    path = null;
        //    string tempPath = Path.GetTempPath() + Path.GetFileNameWithoutExtension(Path.GetTempFileName());

        //    //Берём одно письмо с сервера
        //    if (client.GetEmail(messageNumber, out m))
        //    {
        //        //Сохраняем прикрепления
        //        if (!m.SaveAttachments(tempPath, out errorMsg))
        //        {
        //            errorMsg = string.Format("Ошибка сохранения прикреплений messageNumber: {0}", messageNumber);
        //            return false;
        //        }
        //        path = tempPath;
        //    }
        //    else
        //    {
        //        /*Попадаем если сообщение не принято с почтового сервера*/
        //        errorMsg = string.Format("Ошибка приёма письма № {0} client.GetEmail(i, out m),с сервера {1}", messageNumber, serverAddress);
        //        return false;
        //    }
        //    return true;
        //}

        /// <summary>
        /// Приём почтового сообщения с сервера по номеру
        /// </summary>
        /// <param name="messageNumber"></param>
        /// <param name="mailMessage"></param>
        /// <returns></returns>
        public bool RecieveMessage(int messageNumber, out RxMailMessage mailMessage)
        {

            //Берём одно письмо с сервера
            if (Client.GetEmail(messageNumber, out mailMessage))
            {
                /*Попадаем если сообщение принято с почтового сервера*/
            }
            else
            {
                /*Попадаем если сообщение не принято с почтового сервера*/
                //GlobalLoggin.CallError(string.Format("Ошибка приёма письма № {0} client.GetEmail(i, out m),с сервера {1}",
                //                         messageNumber, serverAddress));

                TextfileLog.AddError(string.Format("Ошибка приёма письма № {0} client.GetEmail(i, out m),с сервера {1}",
                                         messageNumber, serverAddress));
                mailMessage = new RxMailMessage();
                return false;
            }
            return true;
        }

        public bool Connect()
        {
            try
            {
                Client.Connect();
            }
            catch (Exception ex)
            {
                //GlobalLoggin.CallError(ex);
                TextfileLog.AddEntry(ex);
                return false;
            }
            return true;
        }

        public bool Disconnect()
        {
            try
            {
                Client.Disconnect();
            }
            catch (Exception ex)
            {
                //GlobalLoggin.CallError(ex);
                TextfileLog.AddEntry(ex);
                return false;
            }
            return true;
        }

        public void Logging(string message)
        {
            string fileName = String.Format(@"{0}\Log.txt", Environment.CurrentDirectory);
            FileStream fs = new FileStream(fileName, FileMode.Append, FileAccess.Write);
            StreamWriter streamWriter = new StreamWriter(fs);
            streamWriter.Write(string.Format("{0} {1}", DateTime.Now, message + Environment.NewLine));
            streamWriter.Close();
            fs.Close();
        }

        private static void MailLogs(string message)
        {
            string fileName = String.Format(@"{0}\MailLog.txt", Environment.CurrentDirectory);
            FileStream fs = new FileStream(fileName, FileMode.Append, FileAccess.Write);
            StreamWriter streamWriter = new StreamWriter(fs);
            streamWriter.Write(string.Format("{0} {1}", DateTime.Now, message + Environment.NewLine));
            streamWriter.Close();
            fs.Close();
        }
    }
}
