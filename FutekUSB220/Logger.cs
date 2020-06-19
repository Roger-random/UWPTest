using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Foundation.Diagnostics;
using Windows.Storage;

namespace FutekUSB220
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
            recentCount = 10;
            recentLogs = new Queue<String>(recentCount);
        }

        public async void OpenAsync()
        {
            if (logFile == null || !logFile.IsAvailable)
            {
                string logFileName = DateTime.UtcNow.ToString("yyyyMMddHHmmssff") + ".log";
                logFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync(logFileName,
                    Windows.Storage.CreationCollisionOption.OpenIfExists);
                Debug.WriteLine($"Opened log file {logFile.Path}");
                Log("Log file opened.", LoggingLevel.Information);
                Log(logFile.Path, LoggingLevel.Information);
            }
            else
            {
                Log("Ignoring extraneous call to Logger.Open(), this is normal for OnResuming", LoggingLevel.Warning);
            }
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

        public async Task WriteLogBlock()
        {
            // Potential infinite loop here, put back in after figure out how to address
            // Log("Writing out log block", LoggingLevel.Information);

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

        private async Task AddLogLine(string logLine, LoggingLevel level)
        {
            Debug.WriteLine(logLine);
            logBlock.Add(logLine);
            if (logBlock.Count >= LOG_BLOCK_MAX)
            {
                await WriteLogBlock();
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

        public async void Log(string message, LoggingLevel level = LoggingLevel.Verbose)
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
            await AddLogLine(logLine, level);
            while (null != (msgLine = sr.ReadLine()))
            {
                logLine = String.Format($"                   {msgLine}");
                await AddLogLine(logLine, level);
            }
        }
    }
}
