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
        private StorageFile logFile;

        private const int LOG_BLOCK_MAX = 1000;
        private List<String> logBlock;
        private const int LOG_BLOCK_QUEUE_SIZE = 5;
        private Queue<List<String>> logBlockQueue;


        public Logger()
        {
            logFile = null;
            logBlock = new List<String>(LOG_BLOCK_MAX);
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

        public string SampleString(string raw, int maxLength = 80)
        {
            int max = maxLength;
            int now = 0;

            if (raw.Length < maxLength)
            {
                max = raw.Length;
            }

            for (now = 0; now < max; now++)
            {
                char nowchar = raw[now];
                if (Char.IsControl(nowchar) ||
                    !(Char.IsLetterOrDigit(nowchar) ||
                        Char.IsWhiteSpace(nowchar) ||
                        Char.IsPunctuation(nowchar) ||
                        Char.IsSeparator(nowchar) ||
                        Char.IsSymbol(nowchar)))
                {
                    break;
                }
            }

            if (now > 0)
            {
                return raw.Substring(0, now);
            }
            else
            {
                return String.Empty;
            }
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

        public void Log(string message, LoggingLevel level = LoggingLevel.Verbose)
        {
            string logText = String.Format("{0} {1} {2}",
                DateTime.UtcNow.ToString("yyyyMMddHHmmssff"),
                (int)level,
                SampleString(message));

            Debug.WriteLine(logText);
            logBlock.Add(logText);
            if (logBlock.Count >= LOG_BLOCK_MAX)
            {
                WriteLogBlock();
            }
        }
    }
}
