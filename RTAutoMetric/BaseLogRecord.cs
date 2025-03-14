using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RTAutoMetric
{
    public class BaseLogRecord
    {
        public enum LogLevel { General, Warning, Debug, Error };

        private void Log(string dirname, string filename, string logmessage)
        {
            if (!Directory.Exists(dirname))
                Directory.CreateDirectory(dirname);
            if (!File.Exists(filename))
                File.Create(filename).Close();
            using (StreamWriter sw = File.AppendText(filename))
            {
                sw.WriteLine($"{DateTime.Now.ToLongDateString()} {DateTime.Now.ToLongTimeString()} - {logmessage}");
            }

        }

        private void DisplayLog(string logmessage, Color color, RichTextBox rtb)
        {
            rtb.AppendText(DateTime.Now.ToLongDateString() + "," + DateTime.Now.ToLongTimeString() + ">" + logmessage + "\n");
            rtb.Foreground = new SolidColorBrush(color);
            rtb.ScrollToEnd();
            rtb.UpdateLayout();
        }

        public void WriteLog(string logmessage, LogLevel loglevel, RichTextBox rtb)
        {
            string dirname = null;
            string filename = null;
            switch (loglevel)
            {
                case LogLevel.General:
                    dirname = AppDomain.CurrentDomain.BaseDirectory + @"\Logger\General\";
                    filename = dirname + "GeneralLog_" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
                    DisplayLog(logmessage, Colors.Black, rtb);
                    Log(dirname, filename, logmessage);
                    break;
                case LogLevel.Warning:
                    dirname = AppDomain.CurrentDomain.BaseDirectory + @"\Logger\Warning\";
                    filename = dirname + "WarningLog_" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
                    DisplayLog(logmessage, Colors.Orange, rtb);
                    Log(dirname, filename, logmessage);
                    break;
                case LogLevel.Debug:
                    dirname = AppDomain.CurrentDomain.BaseDirectory + @"\Logger\Debug\";
                    filename = dirname + "DebugLog_" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
                    DisplayLog(logmessage, Colors.Blue, rtb);
                    Log(dirname, filename, logmessage);
                    break;
                case LogLevel.Error:
                    dirname = AppDomain.CurrentDomain.BaseDirectory + @"\Logger\Error\";
                    filename = dirname + "ErrorLog_" + DateTime.Now.ToString("yyyyMMdd") + ".txt";
                    DisplayLog(logmessage, Colors.Red, rtb);
                    Log(dirname, filename, logmessage);
                    break;
            }
        }

    }
}
