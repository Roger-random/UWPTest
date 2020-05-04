using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Windows.Foundation.Diagnostics;
using Windows.Storage;

namespace PollingComms
{
    public class Logger
    {
        private StorageFile logFile;

        private const int LOG_BLOCK_MAX = 1000;
        private List<String> logBlock;
        private const int LOG_BLOCK_QUEUE_SIZE = 5;
        private Queue<List<String>> logBlockQueue;

        // A subset of recent logs are kept with constrained size and logging level
        private Queue<String> recentLogs;
        private LoggingLevel recentLevel;
        private int recentCount;

        public Logger()
        {
            logFile = null;
            logBlock = new List<String>(LOG_BLOCK_MAX);
            logBlockQueue = new Queue<List<String>>(LOG_BLOCK_QUEUE_SIZE);

            recentLevel = LoggingLevel.Information;
            recentCount = 5;
            recentLogs = new Queue<String>(recentCount);
        }

        public async void Open()
        {
            if (logFile == null)
            {
                string logFileName = DateTime.UtcNow.ToString("yyyyMMddHHmmssff") + ".log";
                logFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(logFileName,
                    Windows.Storage.CreationCollisionOption.OpenIfExists);
                Log("Logging begins.");
            }
            else
            {
                Warning("Ignoring extraneous call to Logger.Open()");
            }
        }

        public void Close()
        {
            Log("Closing log file.");
            WriteLogBlock();
        }

        public string Recent
        {
            get
            {
                string concat = String.Empty;
                foreach(String line in recentLogs)
                {
                    concat = concat + '\n' + line;
                }
                return concat;
            }
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

        private async void WriteLogBlock()
        {
            List<String> oldBlock = logBlock;
            logBlock = new List<String>(LOG_BLOCK_MAX);
            if (logBlockQueue.Count >= LOG_BLOCK_QUEUE_SIZE)
            {
                // Remove the oldest block of logs
                logBlockQueue.Dequeue();
            }
            logBlockQueue.Enqueue(oldBlock);
            await FileIO.AppendLinesAsync(logFile, oldBlock);
        }

        private void AddLogLine(string logLine, LoggingLevel level)
        {
            Debug.WriteLine(logLine);
            logBlock.Add(logLine);
            if (logBlock.Count >= LOG_BLOCK_MAX)
            {
                WriteLogBlock();
            }

            if (level >= recentLevel)
            {
                recentLogs.Enqueue(logLine);
                if (recentLogs.Count > recentCount)
                {
                    recentLogs.Dequeue();
                }
            }
        }

        public void Log(string message, LoggingLevel level = LoggingLevel.Verbose)
        {
            StringReader sr = null;
            string msgLine = null;
            string logLine = null;

            if (message == null)
            {
                // Logging an entry with no message is not allowed
                throw new ArgumentNullException("message");
            }
            sr = new StringReader(message);
            msgLine = sr.ReadLine();
            logLine = String.Format("{0} {1} {2}",
                DateTime.UtcNow.ToString("yyyyMMddHHmmssff"),
                (int)level,
                msgLine);
            AddLogLine(logLine, level);
            while (null != (msgLine = sr.ReadLine()))
            {
                logLine = String.Format($"                   {msgLine}");
                AddLogLine(logLine, level);
            }
        }
    }
}
