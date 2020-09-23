using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        private SerialDevice _serialDevice = null;
        private Logger _logger = null;
        private DataReader _dataReader = null;
        private DataWriter _dataWriter = null;

        public CommandResponse(Logger logger)
        {
            _logger = logger;
        }

        public async Task<bool> IsDeviceOnPort(string deviceId)
        {
            bool noDataStreaming = false;
            String sampleData = null;

            _logger.Log($"Checking if this is a {LABEL}: {deviceId}", LoggingLevel.Information);
            try
            {
                _serialDevice = await SerialDevice.FromIdAsync(deviceId);
                if (_serialDevice != null)
                {
                    _serialDevice.BaudRate = 4800;
                    _serialDevice.DataBits = 7;
                    _serialDevice.Parity = SerialParity.Even;
                    _serialDevice.StopBits = SerialStopBitCount.Two;
                    _serialDevice.ReadTimeout = new TimeSpan(0, 0, 1);
                    _serialDevice.WriteTimeout = new TimeSpan(0, 0, 1);

                    _dataWriter = new DataWriter(_serialDevice.OutputStream);
                    _dataWriter.UnicodeEncoding = UnicodeEncoding.Utf8;

                    _dataReader = new DataReader(_serialDevice.InputStream);
                    _dataReader.UnicodeEncoding = UnicodeEncoding.Utf8;

                    try
                    {
                        sampleData = await nextData();
                    }
                    catch(TaskCanceledException)
                    {
                        // We timed out trying to read data, which is expected and desirable because this
                        // type of device does not send data until command is sent.
                        noDataStreaming = true;
                    }
                    

                    if (noDataStreaming)
                    {
                        // We verified this device is not sending data without prompt. So now we send a
                        // prompt to see if it answers.
                        _dataWriter.WriteString("?");
                    await _dataWriter.StoreAsync();

                        try
                        {
                            sampleData = await nextData();
                            if (sampleData == null)
                            {
                                _logger.Log($"Null data response to command, {LABEL} is not on {deviceId}");
                                return false;
                            }
                            else
                            {
                                _logger.Log($"Saw response to command, {LABEL} is on {deviceId}");
                                return true;
                            }
                        }
                        catch (TaskCanceledException)
                        {
                            _logger.Log($"TaskCanceledException response to command, {LABEL} is not on {deviceId}");
                        }
                    }
                    else
                    {
                        _logger.Log($"Saw data before issuing command, {LABEL} is not on {deviceId}");
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
                _dataWriter?.Dispose();
                _serialDevice?.Dispose();
            }
            return false;
        }

        private async Task<string> nextData()
        {
            if (_serialDevice == null)
            {
                InvalidOperation("ReadSensorReport called before connecting serial device.");
            }
            if (_dataReader == null)
            {
                InvalidOperation("ReadSensorReport called before connecting data reader.");
            }

            CancellationTokenSource cancelSrc = new CancellationTokenSource(2000); // Cancel after 2000 milliseconds
            uint loadedSize = await _dataReader.LoadAsync(64).AsTask<uint>(cancelSrc.Token);
            if (loadedSize >= 10)
            {
                return _dataReader.ReadString(loadedSize);
            }
            else
            {
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
