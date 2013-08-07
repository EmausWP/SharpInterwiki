using System;
using System.IO;

namespace SharpInterwiki
{
    public class InterwikiLogger
    {
        private readonly string _logFile;
        private readonly int _logLevel;

        public InterwikiLogger (string logFile, int logLevel)
        {
            _logFile = logFile;
            _logLevel = logLevel;
            if (!Path.IsPathRooted(logFile))
            {
                _logFile = Path.GetFullPath(_logFile);
            }
            var dir = Path.GetDirectoryName(_logFile);
            if (dir != null) 
                Directory.CreateDirectory(dir);
        }

        public void LogData(string logstring, int level)
        {
            if (level < _logLevel) 
                return;
            if(string.IsNullOrEmpty(_logFile))
                return;

            var timestamp = DateTime.UtcNow;
            var currentLogFile = _logFile.Replace("%d", timestamp.ToString("yyyy-MM-dd"));
            var fullLogString = string.Format("[{0:HH:mm:ss}] {1}", timestamp, logstring);

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(currentLogFile, true))
                    {
                        sw.WriteLine(fullLogString);
                    }
                    Console.WriteLine(fullLogString);
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    System.Threading.Thread.Sleep(100);
                }
            }
        }

        public void LogData(string logstring, string parameter, int level)
        {
            var fullLogString = string.Format("{0}\t{1}", logstring, parameter);
            LogData(fullLogString, level);
        }
    }
}
