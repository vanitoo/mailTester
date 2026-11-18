using System;
using System.Collections.Generic;
using System.Text;

namespace Tbc.Common.Mail.Pop3.Mime
{
    public class EmailError
    {
        public delegate void ErrorHandler(string ErrorText);

        public event ErrorHandler Error;

        public void CallError(string text, params object[] parameters)
        {
            if (Error != null)
            {
                Error(DateTime.Now.ToString("hh:mm:ss ") + " " + string.Format(text, parameters));
            }
        }
    }
    public delegate void ProgressHandler(int progress,string text,int messCount,Object stateInfo);
    public delegate void DownloadCompleteHandler();
    public delegate void UpdateCompleteHandler();
    //public delegate void LogHandler(string ErrorText);
}
