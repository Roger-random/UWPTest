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
    class CommandResponse
    {
        private const string LABEL = "command+response device";

        private const int READ_TIMEOUT = 100; // milliseconds
        private const int WRITE_TIMEOUT = 100; // milliseconds

        // Should be longer than READ or WRITE timeout.
        private const int TASK_CANCEL_TIMEOUT = 500;

        // How long to wait between querying position. If this is too short, it
        // appears to starve some shared resources and will affect other serial ports.
        private const int LOOP_DELAY = 100;

        // Expected data format: "+00.00000\r"
        private const int EXPECTED_LENGTH = 10;
        private const string DELIMITER = "\r";

        private SerialDevice _serialDevice = null;
        private Logger _logger = null;
        private DataReader _dataReader = null;
        private DataWriter _dataWriter = null;

        public CommandResponse(Logger logger)
        {
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

        public async Task<bool> Connect(string deviceId)
        {
            bool noDataStreaming = false;
            double sampleData = 0.0;
            bool connected = false;

            _serialDevice = await SerialDevice.FromIdAsync(deviceId);
            if (_serialDevice != null)
            {
                _serialDevice.BaudRate = 4800;
                _serialDevice.DataBits = 7;
                _serialDevice.Parity = SerialParity.Even;
                _serialDevice.StopBits = SerialStopBitCount.Two;
                _serialDevice.ReadTimeout = new TimeSpan(0, 0, 0, 0, READ_TIMEOUT /* milliseconds */);
                _serialDevice.WriteTimeout = new TimeSpan(0, 0, 0, 0, WRITE_TIMEOUT /* milliseconds */);

                _dataWriter = new DataWriter(_serialDevice.OutputStream);
                _dataWriter.UnicodeEncoding = UnicodeEncoding.Utf8;

                _dataReader = new DataReader(_serialDevice.InputStream);
                _dataReader.UnicodeEncoding = UnicodeEncoding.Utf8;

                try
                {
                    sampleData = await nextData(2000); // 2 second timeout for the first read.
                    _logger.Log($"Expected no data but retrieved {sampleData}, this is not {LABEL}");
                    noDataStreaming = false;
                }
                catch(TaskCanceledException)
                {
                    // We timed out trying to read data, which is expected and desirable because this
                    // type of device does not send data until command is sent.
                    noDataStreaming = true;
                }
                catch(IOException)
                {
                    // An IO error, however, is a failure condition and probably means we're not
                    // looking at the right port.
                    _logger.Log($"Encountered IO error, indicating {LABEL} is not at {deviceId}");
                }

                if (noDataStreaming)
                {
                    // We verified this device is silent when unprompted. Now we send a prompt to
                    // see if it answers.
                    await sendQuery();

                    try
                    {
                        // Follow-up reads should be fast, don't need 2 second timeout.
                        sampleData = await nextData();
                        _logger.Log($"Saw response {sampleData} to command, {LABEL} is on {deviceId}");
                        connected = true;
                    }
                    catch (TaskCanceledException)
                    {
                        _logger.Log($"Timed out after command, {LABEL} is not on {deviceId}");
                    }
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
            _dataWriter?.Dispose();
            _dataWriter = null;
            _serialDevice?.Dispose();
            _serialDevice = null;
        }

        private Task sendQuery(int cancelTimeout = TASK_CANCEL_TIMEOUT)
        {
            CancellationTokenSource cancelSrc = new CancellationTokenSource(cancelTimeout);

            _dataWriter.WriteString("?");
            return _dataWriter.StoreAsync().AsTask<uint>(cancelSrc.Token);
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
            return Regex.IsMatch(inputData, "^[+-]\\d+.\\d+\r$");
        }

        private async Task<double> nextData(int cancelTimeout = TASK_CANCEL_TIMEOUT)
        {
            string inString = null;
            double parsedValue = 0.0;

            if (_serialDevice == null)
            {
                InvalidOperation("ReadSensorReport called before connecting serial device.");
            }
            if (_dataReader == null)
            {
                InvalidOperation("ReadSensorReport called before connecting data reader.");
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

                if (!inString.EndsWith(DELIMITER))
                {
                    IOError($"{LABEL} response did not end with expected deliminiter {DELIMITER}");
                }

                if (!validFormat(inString))
                {
                    IOError($"Improper format string {inString}");
                }

                parsedValue = Double.Parse(inString);
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
