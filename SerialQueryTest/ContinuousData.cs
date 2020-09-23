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
using Windows.UI.Core;
using UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding;

namespace SerialQueryTest
{
    class ContinuousDataEventArgs : EventArgs
    {
        public ContinuousDataEventArgs(double value)
        {
            Value = value;
        }

        public double Value { get; }
    }

    // Test class representing serial devices that continuously emit data
    class ContinuousData
    {
        private const string LABEL = "continuous data device";
        private const int TASK_CANCEL_TIMEOUT = 250; // milliseconds

        // How long to wait between querying position. If this is too short, it
        // appears to starve some shared resources and will affect other serial ports.
        private const int LOOP_DELAY = 50;

        // Expected format: "+0.00000     lbs\r\n"
        private const int EXPECTED_LENGTH = 18;
        private const int DATA_LENGTH = 13; // When parsing as double, stop just before this character index.
        private const string DELIMITER = "\r\n";

        // How long to wait before retrying connection, in milliseconds
        private const int RETRY_DELAY = 5000;

        private SerialDevice _serialDevice = null;
        private string _serialDeviceId = null;
        private Logger _logger = null;
        private DataReader _dataReader = null;
        private CoreDispatcher _dispatcher = null;
        private bool _shouldReconnect = false;

        public delegate void ContinuousDataEventHandler(object sender, ContinuousDataEventArgs args);
        public event ContinuousDataEventHandler ContinousDataEvent;

        public ContinuousData(CoreDispatcher dispatcher, Logger logger)
        {
            _dispatcher = dispatcher;
            _logger = logger;
        }

        // Connect to serial device on given deviceId and see if it acts like the target device
        public async Task<bool> IsDeviceOnPort(string deviceId)
        {
            bool success = false;

            _logger.Log($"Checking if this is a {LABEL}: {deviceId}", LoggingLevel.Information);
            try
            {
                success = await Connect(deviceId);
            }
            catch (Exception e)
            {
                // Since this is not during actual use, treat as informational rather than error.
                _logger.Log($"Exception thrown while checking if {LABEL} is on {deviceId}", LoggingLevel.Information);
                _logger.Log(e.ToString(), LoggingLevel.Information);
            }
            finally
            {
                Disconnect();
            }
            return success;
        }

        // Connect to serial device on given deviceId
        public async Task<bool> Connect(string deviceId)
        {
            double sampleData = 0.0;
            bool connected = false;

            _serialDevice = await SerialDevice.FromIdAsync(deviceId);
            if (_serialDevice != null)
            {
                _serialDeviceId = deviceId;

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

                    connected = true;
                    _shouldReconnect = true;

                    // Kick off the read loop
                    _ = _dispatcher.RunAsync(CoreDispatcherPriority.Low, ReadNextData);
                }
                catch (TaskCanceledException)
                {
                    _logger.Log($"Timed out, inferring {LABEL} is not at {deviceId}");
                }
                catch (IOException)
                {
                    _logger.Log($"Encountered IO error, indicating {LABEL} is not at {deviceId}");
                }
            }
            else
            {
                // The given device ID cannot be opened.
                _logger.Log("Unable to open {deviceId}", LoggingLevel.Error);
            }

            if (!connected)
            {
                // Connection failed, clean everything up.
                Disconnect();
            }

            return connected;
        }

        // Dispose and clear all handles to serial device
        public void Disconnect()
        {
            _logger.Log($"Disconnecting {LABEL}");
            _dataReader?.Dispose();
            _dataReader = null;
            _serialDevice?.Dispose();
            _serialDevice = null;
        }

        private async void ReadNextData()
        {
            if (null == _dataReader)
            {
                // Serial device disconnected while we were waiting in the dispatcher queue.
                return;
            }

            try
            {
                // Read the new value and send it to subscribers
                double newValue = await nextData();
                OnContinuousDataEvent(new ContinuousDataEventArgs(newValue));

                // Take a short break before continuing the read loop
                await Task.Delay(LOOP_DELAY);
                _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, ReadNextData);
            }
            catch (Exception e)
            {
                _logger.Log($"{LABEL} ReadNextData failed, read loop halted.", LoggingLevel.Error);
                _logger.Log(e.ToString(), LoggingLevel.Error);
                if (_shouldReconnect)
                {
                    _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, Reconnect);
                }
            }
        }

        private async void Reconnect()
        {
            bool success = false;

            Disconnect();
            if (_shouldReconnect)
            {
                _logger.Log($"Reconnecting to {LABEL}", LoggingLevel.Information);
                try
                {
                    success = await Connect(_serialDeviceId);
                }
                catch (Exception e)
                {
                    _logger.Log($"Failed to reconnect to {LABEL}", LoggingLevel.Information);
                    _logger.Log(e.ToString(), LoggingLevel.Information);
                }

                if (!success)
                {
                    await Task.Delay(RETRY_DELAY);
                    _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, Reconnect);
                }
            }
        }

        protected virtual void OnContinuousDataEvent(ContinuousDataEventArgs e)
        {
            ContinuousDataEventHandler raiseEvent = ContinousDataEvent;

            if (raiseEvent != null)
            {
                raiseEvent(this, e);
            }
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
