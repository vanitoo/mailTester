using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Tbc.Common.Mail.Pop3.Mime
{
    public class MyDecoder
    {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inString"></param>
        /// <param name="enc"></param>
        private static string ConvertToWin(string inString, Encoding enc)
        {
            string outputString = null;
            string inputString = inString.Replace("=\n", "").Replace("_", " ");
            if (inputString.Length > 3)
            {
                // initialise output string ...
                outputString = "";
                for (int x = 0; x < inputString.Length; )
                {
                    string s1 = inputString.Substring(x, 1);
                    if ((s1.Equals("=")) && ((x + 2) < inputString.Length))
                    {
                        string hexString = inputString.Substring(x + 1, 2);
                        // if hexadecimal ...
                        if (Regex.Match(hexString.ToUpper()
                         , @"^[A-F|0-9]+[A-F|0-9]+$").Success)
                        {
                            // convert to string representation ...
                            //System.Text.Encoding koi8r = System.Text.Encoding.GetEncoding("koi8-r");
                            outputString +=
                             enc.GetString(new byte[] { System.Convert.ToByte(hexString, 16) });
                            x += 3;
                        }
                        else
                        {
                            outputString += s1;
                            ++x;
                        }
                    }
                    else
                    {
                        outputString += s1;
                        ++x;
                    }
                }
                inString = outputString;
            }
            else
            {
                outputString = inputString;
            }
            return inString;
        }

        /// <summary>
        /// FromBase64
        /// </summary>
        /// <param name="base64"></param>
        /// <param name="enc"></param>
        /// <returns></returns>
        public static bool FromBase64(string base64, Encoding enc, out string outString)
        {
            outString = null;

            try
            {
                byte[] bytes = System.Convert.FromBase64String(base64);
                outString = enc.GetString(bytes);
                return true;
            }

            catch
            {
                return false;
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <param name="src"></param>
        /// <param name="trg"></param>
        /// <returns></returns>
        public static string Convert(string value, Encoding src, Encoding trg)
        {
            Decoder dec = src.GetDecoder();
            byte[] ba = trg.GetBytes(value);
            int len = dec.GetCharCount(ba, 0, ba.Length);
            char[] ca = new char[len];
            dec.GetChars(ba, 0, ba.Length, ca, 0);
            return new string(ca);
        }

        public static string FileReName(string oldFileName, string newFileName)
        {
            string ext = null;
            string[] tmp = oldFileName.Split('.');
            try
            {
                ext = tmp[tmp.Length - 1];
                return newFileName + "." + ext;
            }
            catch { return newFileName; }

        }
        //-------------------------------

        public static string ConvertToWin(string inString)
        {
            try
            {
                //Проверка на пустоту
                if (inString == null || inString == "") return inString;

                System.Text.Encoding enc = Encoding.Default;
                //bool base64 = false;
                string outString = null, outStr = null;

                //Разбиваем строку на массив
                string[] ourStrings = inString.Split(' '); string[] ourEndStrings;
                for (int i = 0; i < ourStrings.Length; i++)
                {
                    ourStrings[i] = ourStrings[i].Remove(0, 2);
                    ourStrings[i] = ourStrings[i].Remove(ourStrings[i].Length - 2, 2);
                    ourEndStrings = ourStrings[i].Split('?');
                    if (ourEndStrings != null && ourEndStrings.Length > 1)
                    {
                        enc = Encoding.GetEncoding(ourEndStrings[0]);
                        if (ourEndStrings[1].ToLower() == "b")
                        {
                            FromBase64(ourEndStrings[2], enc, out outStr);
                            outString = outString + outStr;
                        }
                        else { outString = outString + ConvertToWin(ourEndStrings[2], enc); }
                    }
                    else { return inString; }

                }
                return outString;
            }
            catch { return inString; }
        }
    }
}
