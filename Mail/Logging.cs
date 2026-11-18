using System;
using System.IO;
using System.Text;

namespace Tbc.Common.Logging
{
    public static class GlobalLoggin
    {
        public delegate void ErrorHandler(string errorText);
        public delegate void WarningHandler(string errorText);
        public delegate void TraceHandler(string errorText);
        public static event ErrorHandler Error;
        public static event WarningHandler Warning;
        public static event TraceHandler Trace;

        private const string dateMask = "yyyy-MM-dd HH:mm:ss";
        public static void CallError(string text, params object[] parameters)
        {
            if (Error != null)
            {
                Error(MarkString(string.Format(text, parameters), ErrorLevel.Error));
                //Error(String.Format("{0} {1}", DateTime.Now.ToString(dateMask), string.Format(text, parameters)));
            }
        }
        public static void CallError(Exception ex)
        {
            if (Error != null)
            {
                Error(MarkString(string.Format("Îøèáêà! StackTrace: {0}{1} Message: {2}!", ex.StackTrace, Environment.NewLine, ex.Message), ErrorLevel.Error));
            }
        }
        public static void CallWarning(string text, params object[] parameters)
        {
            if (Warning != null)
            {
                Warning(MarkString(string.Format(text, parameters), ErrorLevel.Warning));
            }
        }
        public static void CallTrace(string text, params object[] parameters)
        {
            if (Trace != null)
            {
                Trace(MarkString(string.Format(text, parameters), ErrorLevel.Logging));
                //Trace(String.Format("{0} {1}", DateTime.Now.ToString(dateMask), string.Format(text, parameters)));
            }
        }

        public static void CallErrorEmtyString()
        {
            Error("***");
        }

        public static void CallWarningEmtyString()
        {
            Warning("***");
        }

        public static void CallTraceEmtyString()
        {
            Trace("***");
        }
        public static string MarkString(string text, ErrorLevel Level)
        {
            string date = DateTime.Now.ToString(dateMask);
            StringBuilder sb = new StringBuilder();
            switch (Level)
            {
                case ErrorLevel.Error:
                    sb.Append(string.Format("{0} [ERROR] {1}", date, text));
                    break;
                case ErrorLevel.Information:
                    sb.Append(string.Format("{0} [INFO] {1}", date, text));
                    break;
                case ErrorLevel.Logging:
                    sb.Append(string.Format("{0} [LOG] {1}", date, text));
                    break;
                case ErrorLevel.Warning:
                    sb.Append(string.Format("{0} [WARNING] {1}", date, text));
                    break;
                default:
                    sb.Append(string.Format("{0} [LOG] {1}", date, text));
                    break;
            }
            return sb.ToString();
        }
    }

    public static class TextfileLog
    {
        public const string DateMask = "yyyy-MM-dd HH:mm:ss";
        public static bool CopyToConsole;

        public static void Write(string Text)
        {
            if (!Directory.Exists(String.Format(@"{0}\Logs", System.Windows.Forms.Application.StartupPath)))
            {
                Directory.CreateDirectory(String.Format(@"{0}\Logs", System.Windows.Forms.Application.StartupPath));
            }

            StreamWriter fs;

            try
            {
                fs = new StreamWriter(Log, true);
                fs.WriteLine("{" + Text + "}");
                fs.Flush();
                fs.Close();
            }
            catch { }


            if (CopyToConsole)
                Console.WriteLine(Text);
        }

        public static void AddEntry(string Text, ErrorLevel Level)
        {
            string entry = DateTime.Now.ToString(DateMask) + " ";

            switch (Level)
            {
                case ErrorLevel.Error:
                    entry += "[ERROR] ";
                    break;
            
                case ErrorLevel.Information:
                    entry += "[INFO] ";
                    break;
                case ErrorLevel.Logging:
                    entry += "[LOG] ";
                    break;
                case ErrorLevel.Warning:
                    entry += "[WARNING] ";
                    break;
                default:
                    entry += "[LOG] ";
                    break;
            }

            entry += Text;

            Write(entry);
        }

        public static void AddEntry(Exception ex)
        {
            string entry = String.Format("{0} [ERROR] ", DateTime.Now.ToString(DateMask));
            entry += ex.Message;
            if (ex.InnerException != null)
            {
                entry += String.Format("[{0}]", ex.InnerException.Message);
            }

            entry += String.Format("[STACKTRACE: {0}]", ex.StackTrace);

            Write(entry);
        }

        public static void AddInfo(string info)
        {
            AddEntry(info, ErrorLevel.Information);
        }

        public static void AddLog(string info)
        {
            AddEntry(info, ErrorLevel.Logging);
        }

        public static void AddError(string info)
        {
            AddEntry(info, ErrorLevel.Error);
        }

        public static void AddWarning(string info)
        {
            AddEntry(info, ErrorLevel.Warning);
        }

        public static string GetLogFile(DateTime dt)
        {
            return (String.Format(@"{0}\Logs\{1:[yyyy-MM-dd]}Log.txt", System.Windows.Forms.Application.StartupPath, dt));
        }

        public static string Log
        {
            get
            {
                int i = 0;
                while (true)
                {
                    string l = String.Format(@"{0}\Logs\{1:[yyyy-MM-dd]}Log{2}.txt",
                                             System.Windows.Forms.Application.StartupPath, DateTime.Today,
                                             i == 0 ? "" : i.ToString());

                    if (!File.Exists(l)) break;

                    FileInfo FI = new FileInfo(l);
                    if (FI.Length > 0x100 * 0x1000) i++;
                    else
                    {
                        break;
                    }
                }

                return String.Format(@"{0}\Logs\{1:[yyyy-MM-dd]}Log{2}.txt",
                                             System.Windows.Forms.Application.StartupPath, DateTime.Today,
                                             i == 0 ? "" : i.ToString());
            }
        }
    }

    public enum ErrorLevel
    {
        Error = 0,
        Warning = 1,
        Information = 2,
        Logging = 3
    }
}
