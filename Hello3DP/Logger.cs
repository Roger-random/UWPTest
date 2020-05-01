using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Foundation.Diagnostics;
using Windows.Storage;

namespace Hello3DP
{
    public class Logger
    {
        class LogEntry
        {
            public DateTime TimeStamp { get; }
            public string Message { get; }
            public LoggingLevel Level { get; }

            public LogEntry(string message, LoggingLevel level = LoggingLevel.Verbose)
            {
                TimeStamp = DateTime.UtcNow;
                Message = message;
                Level = level;
            }
        }

        private StorageFile logFile;
        //private const int historyMax = 10;
        //private Queue<List<LogEntry>> history;
        //private const int blockMax = 200;
        //private List<LogEntry> currentBlock;

        public Logger()
        {
            logFile = null;
        }

        public async void Open()
        {
            if (logFile == null)
            {
                string logFileName = DateTime.UtcNow.ToString("yyyyMMddHHmmssff") + ".log";
                logFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(logFileName,
                    Windows.Storage.CreationCollisionOption.OpenIfExists);
            }
            else
            {
                Warning("Ignoring extraneous call to Logger.Open()");
            }
        }

        public void Close()
        {
            // TODO: StorageFile doesn't seem to need closing, but we might need other cleanup
        }

        public void Critical(string message)
        {
            Log(message, LoggingLevel.Critical);
        }

        public void Error(string message)
        {
            Log(message, LoggingLevel.Error);
        }

        public void Warning(string message)
        {
            Log(message, LoggingLevel.Warning);
        }

        public void Information(string message)
        {
            Log(message, LoggingLevel.Information);
        }

        public void Verbose(string message)
        {
            Log(message, LoggingLevel.Verbose);
        }

        private async void Log(string message, LoggingLevel level)
        {
            LogEntry entry = new LogEntry(message, level);
            string logText = String.Format("{0} {1} {2}\r\n",
                entry.TimeStamp.ToString("yyyyMMddHHmmssff"),
                (int)entry.Level,
                Regex.Escape(message));

            Debug.WriteLine(logText);
            await FileIO.AppendTextAsync(logFile, logText);
        }
    }
}
