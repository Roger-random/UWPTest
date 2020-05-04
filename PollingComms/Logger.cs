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

        // When we pass this many log entries in a block, make a new block
        private const int LOG_BLOCK_NEW = 1000;
        private List<String> logBlock;
        private const int LOG_BLOCK_QUEUE_SIZE = 5;
        private Queue<List<String>> logBlockQueue;


        public Logger()
        {
            logFile = null;
            logBlock = new List<String>(LOG_BLOCK_NEW); // May exceed this size occasionally.
            logBlockQueue = new Queue<List<String>>(LOG_BLOCK_QUEUE_SIZE);
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
            logBlock = new List<String>(LOG_BLOCK_NEW);
            if (logBlockQueue.Count >= LOG_BLOCK_QUEUE_SIZE)
            {
                // Remove the oldest block of logs
                logBlockQueue.Dequeue();
            }
            logBlockQueue.Enqueue(oldBlock);
            await FileIO.AppendLinesAsync(logFile, oldBlock);
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
            Debug.WriteLine(logLine);
            logBlock.Add(logLine);
            while (null != (msgLine = sr.ReadLine()))
            {
                logLine = String.Format($"                   {msgLine}");
                Debug.WriteLine(logLine);
                logBlock.Add(logLine);
            }
            if (logBlock.Count >= LOG_BLOCK_NEW)
            {
                WriteLogBlock();
            }
        }
    }
}
