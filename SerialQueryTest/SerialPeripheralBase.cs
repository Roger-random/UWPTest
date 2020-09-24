using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Foundation.Diagnostics;
using Windows.Storage.Streams;
using Windows.UI.Core;
using UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding;

namespace SerialQueryTest
{
    // Base class for common functionality when communicating with
    // peripherals connected via serial
    abstract class SerialPeripheralBase
    {
        // How long to wait before cancelling async I/O, in milliseconds
        private const int TASK_CANCEL_TIMEOUT = 2000;

        // How long to wait before retrying connection, in milliseconds
        private const int RETRY_DELAY = 5000;

        private Logger _logger = null;
        private CoreDispatcher _dispatcher = null;

        private SerialDevice _serialDevice = null;
        private string _serialDeviceId = null;
        private DataReader _dataReader = null;
        private bool _shouldReconnect = false;

        public SerialPeripheralBase(CoreDispatcher dispatcher, Logger logger)
        {
            _dispatcher = dispatcher;
            _logger = logger;
        }

        protected IAsyncAction RunAsync(CoreDispatcherPriority priority, DispatchedHandler callback)
        {
            return _dispatcher.RunAsync(priority, callback);
        }

        protected void Log(string message, LoggingLevel level = LoggingLevel.Verbose)
        {
            _logger.Log(message, level);
        }

        protected void IOError(string message)
        {
            Log(message, LoggingLevel.Error);
            throw new IOException(message);
        }

        protected void InvalidOperation(string message)
        {
            Log(message, LoggingLevel.Error);
            throw new InvalidOperationException(message);
        }

        protected async Task<bool> SetupSerialDevice(string deviceId,
            uint baudRate, ushort dataBits, SerialParity parity, SerialStopBitCount stopBits)
        {
            bool success = false;

            _serialDevice = await SerialDevice.FromIdAsync(deviceId);
            if (_serialDevice != null)
            {
                _serialDeviceId = deviceId;

                _serialDevice.BaudRate = baudRate;
                _serialDevice.DataBits = dataBits;
                _serialDevice.Parity = parity;
                _serialDevice.StopBits = stopBits;
                _serialDevice.ReadTimeout = new TimeSpan(0, 0, 0, 0, TASK_CANCEL_TIMEOUT /* milliseconds */);

                _dataReader = new DataReader(_serialDevice.InputStream);
                _dataReader.UnicodeEncoding = UnicodeEncoding.Utf8;

                success = true;
            }
            else
            {
                // The given device ID cannot be opened.
                Log("Unable to open {deviceId}", LoggingLevel.Error);
            }

            return success;
        }

        protected void VerifyReader()
        {
            if (_serialDevice == null)
            {
                InvalidOperation("nextData called before connecting serial device.");
            }
            if (_dataReader == null)
            {
                InvalidOperation("nextData called before connecting data reader.");
            }
        }

        protected bool HaveReader()
        {
            return (null != _dataReader);
        }

        protected Task<uint> ReaderLoadAsync(uint count)
        {
            CancellationTokenSource cancelSrc = new CancellationTokenSource(TASK_CANCEL_TIMEOUT);
            return _dataReader.LoadAsync((uint)count).AsTask<uint>(cancelSrc.Token);
        }

        protected string ReaderReadString(uint codeUnitCount)
        {
            return _dataReader.ReadString(codeUnitCount);
        }

        protected async void Reconnect()
        {
            bool success = false;

            DisposeSerialObjeccts();
            if (_shouldReconnect)
            {
                Log($"Reconnecting to {LABEL}", LoggingLevel.Information);
                try
                {
                    success = await Connect(_serialDeviceId);
                }
                catch (Exception e)
                {
                    Log($"Failed to reconnect to {LABEL}", LoggingLevel.Information);
                    Log(e.ToString(), LoggingLevel.Information);
                }

                if (!success)
                {
                    await Task.Delay(RETRY_DELAY);
                    _ = RunAsync(CoreDispatcherPriority.Normal, Reconnect);
                }
            }
        }

        // Connect to serial device on given deviceId and see if it acts like the target device
        public async Task<bool> IsDeviceOnPort(string deviceId)
        {
            bool success = false;

            Log($"Checking if this is a {LABEL}: {deviceId}", LoggingLevel.Information);
            try
            {
                success = await Connect(deviceId);
            }
            catch (Exception e)
            {
                // Since this is not during actual use, treat as informational rather than error.
                Log($"Exception thrown while checking if {LABEL} is on {deviceId}", LoggingLevel.Information);
                Log(e.ToString(), LoggingLevel.Information);
            }
            finally
            {
                Disconnect();
            }
            return success;
        }

        public abstract Task<bool> Connect(string deviceId);

        public virtual void Disconnect()
        {
            _shouldReconnect = false;
            DisposeSerialObjeccts();
        }

        public virtual void DisposeSerialObjeccts()
        {
            Log($"Disconnecting {LABEL}");
            _dataReader?.Dispose();
            _dataReader = null;
            _serialDevice?.Dispose();
            _serialDevice = null;
        }

        protected bool ShouldReconnect
        {
            get
            {
                return _shouldReconnect;
            }
            set
            {
                _shouldReconnect = value;
            }
        }

        protected abstract string LABEL
        {
            get;
        }
    }
}
