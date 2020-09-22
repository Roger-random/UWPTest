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

                    sampleData = await nextData();
                    if (sampleData == null)
                    {
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                // Since this is not during actual functionality, treat as informational rather than error.
                _logger.Log($"Exception thrown while checking if {LABEL} is on {deviceId}", LoggingLevel.Information);
                _logger.Log(e.ToString(), LoggingLevel.Information);
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
            if (loadedSize > 18)
            {
                return _dataReader.ReadString(32);
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
