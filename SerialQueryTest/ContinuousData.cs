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

        private SerialDevice _serialDevice = null;
        private Logger _logger = null;
        private DataReader _dataReader = null;

        public ContinuousData(Logger logger)
        {
            _logger = logger;
        }

        public async Task<bool> IsDeviceOnPort(string deviceId)
        {
            String sampleData = null;

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
                    _serialDevice.ReadTimeout = new TimeSpan(0, 0, 1);

                    _dataReader = new DataReader(_serialDevice.InputStream);
                    _dataReader.UnicodeEncoding = UnicodeEncoding.Utf8;

                    try
                    {
                        /* Test resync with offset data
                        try
                        {
                            CancellationTokenSource cancelSrc = new CancellationTokenSource(2000); // Cancel after 2000 milliseconds
                            await _dataReader.LoadAsync(1).AsTask<uint>(cancelSrc.Token);
                            _ = _dataReader.ReadString(1);
                        }
                        catch(TaskCanceledException)
                        {
                        }
                        */ 
                        sampleData = await nextData();
                        if (sampleData == null)
                        {
                            _logger.Log($"No string so inferring {LABEL} is not at {deviceId}");
                            return false;
                        }
                        else
                        {
                            _logger.Log($"Look slike {LABEL} is at {deviceId}");
                            return true;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Log($"Timed out so inferring {LABEL} is not at {deviceId}");
                        return false;
                    }
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

        private async Task<string> nextData()
        {
            string inString = null;
            int deliminiterIndex = 0;
            int syncRemainder = 0;

            if (_serialDevice == null)
            {
                InvalidOperation("ReadSensorReport called before connecting serial device.");
            }
            if (_dataReader == null)
            {
                InvalidOperation("ReadSensorReport called before connecting data reader.");
            }

            CancellationTokenSource cancelSrc = new CancellationTokenSource(2000); // Cancel after 2000 milliseconds
            uint loadedSize = await _dataReader.LoadAsync(18).AsTask<uint>(cancelSrc.Token);
            if (loadedSize > 0)
            {
                try
                {
                    inString = _dataReader.ReadString(loadedSize);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // This is thrown when the data can't be parsed as UTF8, interpreting to mean the incoming
                    // data was not set at the baud rate of the expected device.
                    _logger.Log($"Non UTF-8 data encountered. This is only expected when probing ports for {LABEL}");
                    return null;
                }

                deliminiterIndex = inString.IndexOf("\r\n");
                if (deliminiterIndex != 16)
                {
                    // Oh no, the end deliminiter is not where we expected it to be. Can we recover?
                    if (inString.Length < 18)
                    {
                        // Sometimes we just don't get all 18 we asked for and just need to ask for the rest.
                        syncRemainder = 18 - inString.Length;
                    }
                    else if (deliminiterIndex == -1)
                    {
                        // If we have the full length yet there's no delimiter at all in the string we retrieved,
                        // then the problem is worse than just getting offset data.
                        InvalidOperation($"No deliminter in data retrieved from {LABEL}");
                    }
                    else
                    {
                        // Delimiter is somewhere in the middle instead of the end. Discard the end of the
                        // truncated prior segment, keep the start of the latter segment, and try to read data
                        // to complete the latter segment.
                        inString = inString.Substring(deliminiterIndex + 2);
                        syncRemainder = 18 - inString.Length;
                    }

                    if (syncRemainder < 0)
                    {
                        InvalidOperation($"Expected inString.Length to be 18 or less, but is {inString.Length}");
                    }

                    cancelSrc = new CancellationTokenSource(1000); // Cancel after 1 second
                    loadedSize = await _dataReader.LoadAsync((uint)syncRemainder).AsTask<uint>(cancelSrc.Token);

                    if (loadedSize != syncRemainder)
                    {
                        InvalidOperation($"Failed to resync, want {syncRemainder} but got {loadedSize}");
                    }
                    inString += _dataReader.ReadString(loadedSize);

                    if (16 != inString.IndexOf("\r\n"))
                    {
                        InvalidOperation($"Failed to resync, only got {inString.Length} long string: {inString}");
                    }
                }

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
                if (!Regex.IsMatch(inString, "^[+-]\\d+.\\d+\\s+lbs\r\n$"))
                {
                    InvalidOperation($"Improper format string {inString}");
                }
                //value = Double.Parse(reportLine.Substring(0, 13));
                return inString;
            }
            else
            {
                _logger.Log("Unexpected: LoadAsync() returned with zero bytes.", LoggingLevel.Critical);
                return null;
            }
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
