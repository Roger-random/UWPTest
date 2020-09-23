using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
using Windows.Foundation.Diagnostics;
using Windows.Storage.Streams;
using UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding;

namespace SerialQueryTest
{
    // Test class representing serial devices that continuously emit data
    class ContinuousData
    {
        private const string LABEL = "continuous data device";
        private const int TASK_CANCEL_TIMEOUT = 150; // milliseconds
        // Expected format: "+0.00000     lbs\r\n"
        private const int EXPECTED_LENGTH = 18;
        private const int DATA_LENGTH = 13; // When parsing as double, stop just before this character index.
        private const string DELIMITER = "\r\n";

        private SerialDevice _serialDevice = null;
        private Logger _logger = null;
        private DataReader _dataReader = null;

        public ContinuousData(Logger logger)
        {
            _logger = logger;
        }

        public async Task<bool> IsDeviceOnPort(string deviceId)
        {
            double sampleData = 0.0;

            _logger.Log($"Checking if this is a {LABEL}: {deviceId}", LoggingLevel.Information);
            try
            {
                _serialDevice = await SerialDevice.FromIdAsync(deviceId);
                if (_serialDevice != null)
                {
                    _serialDevice.BaudRate = 9600;
                    _serialDevice.DataBits = 8;
                    _serialDevice.Parity = SerialParity.None;
                    _serialDevice.StopBits = SerialStopBitCount.One;
                    _serialDevice.ReadTimeout = new TimeSpan(0, 0, 0, 0, TASK_CANCEL_TIMEOUT /* milliseconds */);

                    _dataReader = new DataReader(_serialDevice.InputStream);
                    _dataReader.UnicodeEncoding = UnicodeEncoding.Utf8;

                    try
                    {
                        sampleData = await nextData(2000); // 2 second timeout for the first read.
                        _logger.Log($"Successfully retrieved {sampleData} from {LABEL} on {deviceId}");

                        return true;
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Log($"Timed out so inferring {LABEL} is not at {deviceId}");
                    }
                    catch (IOException)
                    {
                        _logger.Log($"Encountered error indicating {LABEL} is not at {deviceId}");
                    }
                }
                else
                {
                    // This is a high priority error because we expect be able to open the port to take look.
                    _logger.Log($"Unable to get SerialDevice for {LABEL} on {deviceId}", LoggingLevel.Error);
                }
            }
            catch (Exception e)
            {
                // Since this is not during actual functionality, treat as informational rather than error.
                _logger.Log($"Exception thrown while checking if {LABEL} is on {deviceId}", LoggingLevel.Information);
                _logger.Log(e.ToString(), LoggingLevel.Information);
            }
            finally
            {
                _dataReader?.Dispose();
                _serialDevice?.Dispose();
            }
            return false;
        }

        private bool validFormat(string inputData)
        {
            // Match input string against expected pattern using regular expression
            // C# Regular Expression syntax https://docs.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference
            //
            // ^ = must start with this
            //   [+-] = must have one of either '+' or '-'
            //     ^[+-] = must start with either '+' or '-'
            // \d = numerical digit
            //   + = one or more matches
            //     \d+ = at least one digit
            // . = one and only one decimal point
            // (repeat "at least one digit" above)
            // \s = space & similar spacing characters
            //   + = one or more matches
            //     \s+ = at least one spacing character
            // lbs\r\n = literal string "lbs" followed by carriage return then line feed.
            //   $ = end of the string
            return Regex.IsMatch(inputData, "^[+-]\\d+.\\d+\\s+lbs\r\n$");
        }

        private async Task<double> nextData(int cancelTimeout = TASK_CANCEL_TIMEOUT)
        {
            string inString = null;
            double parsedValue = 0.0;
            int deliminiterIndex = 0;
            int syncRemainder = 0;

            if (_serialDevice == null)
            {
                InvalidOperation("nextData called before connecting serial device.");
            }
            if (_dataReader == null)
            {
                InvalidOperation("nextData called before connecting data reader.");
            }

            CancellationTokenSource cancelSrc = new CancellationTokenSource(cancelTimeout);
            uint loadedSize = await _dataReader.LoadAsync(EXPECTED_LENGTH).AsTask<uint>(cancelSrc.Token);
            if (loadedSize > 0)
            {
                try
                {
                    inString = _dataReader.ReadString(loadedSize);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // This is thrown when the data can't be parsed as UTF8, interpreting to mean the incoming
                    // data was not sent at the baud rate of the expected device.
                    IOError($"Non UTF-8 data encountered. This is expected when {LABEL} is not on this port.");
                }

                deliminiterIndex = inString.IndexOf(DELIMITER);
                if (deliminiterIndex != (EXPECTED_LENGTH-DELIMITER.Length))
                {
                    // Resync Mode: the end deliminiter is not where we expected it to be. Can we recover?
                    if (inString.Length < EXPECTED_LENGTH)
                    {
                        // Occasionally not all 18 requested bytes came in, and we just need to ask for the rest.
                        syncRemainder = EXPECTED_LENGTH - inString.Length;
                    }
                    else if (deliminiterIndex == -1)
                    {
                        // If we have the full length yet there's no delimiter at all in the string we retrieved,
                        // then the problem is worse than just getting offset data.
                        IOError($"No deliminter in data retrieved from {LABEL}");
                    }
                    else
                    {
                        // Delimiter is somewhere in the middle instead of the end. Discard the end of the
                        // truncated prior segment, keep the start of the latter segment, and try to read data
                        // to complete the latter segment.
                        inString = inString.Substring(deliminiterIndex + DELIMITER.Length);
                        syncRemainder = EXPECTED_LENGTH - inString.Length;
                    }

                    if (syncRemainder < 0)
                    {
                        // This means a dumb arithmetic error was made earlier.
                        IOError($"Expected inString.Length to be {EXPECTED_LENGTH} or less, but is {inString.Length}");
                    }

                    cancelSrc = new CancellationTokenSource(cancelTimeout);
                    loadedSize = await _dataReader.LoadAsync((uint)syncRemainder).AsTask<uint>(cancelSrc.Token);
                    if (loadedSize != syncRemainder)
                    {
                        // If we are really just picking up a few straggler bytes, this would be easy.
                        // If it is a problem, something else is wrong.
                        IOError($"Failed to resync, want {syncRemainder} but got {loadedSize}");
                    }

                    // Not checking for ArgumentOutOfRangeException here because if baud rate is wrong we should
                    // have already triggered ArgumentOutOfRangeException in the earlier ReadString().
                    inString += _dataReader.ReadString(loadedSize);

                    if (16 != inString.IndexOf(DELIMITER))
                    {
                        IOError($"Failed to resync, only got {inString.Length} long string: {inString}");
                    }
                }

                if (!validFormat(inString))
                {
                    IOError($"Improper format string {inString}");
                }
                parsedValue =  Double.Parse(inString.Substring(0, DATA_LENGTH));
            }
            else
            {
                IOError("Unexpected: LoadAsync() returned with zero bytes.");
            }

            return parsedValue;
        }

        private void IOError(string message)
        {
            _logger.Log(message, LoggingLevel.Error);
            throw new IOException(message);
        }

        private void InvalidOperation(string message)
        {
            _logger.Log(message, LoggingLevel.Error);
            throw new InvalidOperationException(message);
        }
    }
}
