using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
using Windows.Foundation.Diagnostics;
using Windows.Storage.Streams;
using Windows.UI.Core;
using UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding;

using Com.Regorlas.Logging;

namespace Com.Regorlas.Serial
{
    // Base class for common functionality when communicating with
    // peripherals connected via serial
    abstract class SerialPeripheralBase
    {
        private Logger _logger = null;
        private CoreDispatcher _dispatcher = null;

        private SerialDevice _serialDevice = null;
        private string _serialDeviceId = null;
        private DataReader _dataReader = null;
        private DataWriter _dataWriter = null;
        private bool _shouldReconnect = false;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="dispatcher">Instance of CoreDispatcher to use for queueing tasks</param>
        /// <param name="logger">Instance of simple logger</param>
        public SerialPeripheralBase(CoreDispatcher dispatcher, Logger logger)
        {
            _dispatcher = dispatcher;
            _logger = logger;
        }

        /// <summary>
        /// Send data to the simple logger.
        /// </summary>
        /// <param name="message">Log message</param>
        /// <param name="level">Message priority level</param>
        protected void Log(string message, LoggingLevel level = LoggingLevel.Verbose)
        {
            _logger.Log(message, level);
        }

        /// <summary>
        /// Log an error message and throw IOException with it.
        /// </summary>
        /// <param name="message">Error message</param>
        protected void IOError(string message)
        {
            Log(message, LoggingLevel.Error);
            throw new IOException(message);
        }

        /// <summary>
        /// Log an error message and throw InvalidOperationException with it.
        /// </summary>
        /// <param name="message">Error message</param>
        protected void InvalidOperation(string message)
        {
            Log(message, LoggingLevel.Error);
            throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Configure and open a serial communication channel for sending and receiving strings.
        /// </summary>
        /// <param name="deviceId">Device ID string from DeviceInformation.Id</param>
        /// <returns>True if the serial device has been successfully opened</returns>
        private async Task<bool> SetupSerialDevice(string deviceId)
        {
            bool success = false;

            _serialDevice = await SerialDevice.FromIdAsync(deviceId);
            if (_serialDevice != null)
            {
                _serialDeviceId = deviceId;

                _serialDevice.BaudRate = DeviceBaudRate;
                _serialDevice.DataBits = DeviceDataBits;
                _serialDevice.Parity   = DeviceParity;
                _serialDevice.StopBits = DeviceStopBits;
                _serialDevice.ReadTimeout = new TimeSpan(0, 0, 0, 0, ReadTimeout);
                _serialDevice.WriteTimeout = new TimeSpan(0, 0, 0, 0, WriteTimeout);

                _dataWriter = new DataWriter(_serialDevice.OutputStream)
                {
                    UnicodeEncoding = UnicodeEncoding.Utf8
                };

                _dataReader = new DataReader(_serialDevice.InputStream)
                {
                    UnicodeEncoding = UnicodeEncoding.Utf8
                };

                success = true;
            }
            else
            {
                // The given device ID cannot be opened.
                Log($"Unable to open {DeviceLabel} at {deviceId}", LoggingLevel.Error);
            }

            return success;
        }

        /// <summary>
        /// Connect to serial device on given deviceId
        /// </summary>
        /// <param name="deviceId">Device ID string from DeviceInformation.Id</param>
        /// <returns>Boolean: true if connection was successful</returns>
        public virtual async Task<bool> Connect(string deviceId)
        {
            bool success = false;

            try
            {
                if (await SetupSerialDevice(deviceId))
                {
                    success = await GetSampleData();
                    if (success)
                    {
                        _shouldReconnect = true;

                        // Kick off the communication loop
                        _ = _dispatcher.RunAsync(CoreDispatcherPriority.Low, CommunicationLoop);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                Log($"Timed out, inferring {DeviceLabel} is not at {deviceId}");
            }
            catch (IOException)
            {
                Log($"Encountered IO error, indicating {DeviceLabel} is not at {deviceId}");
            }
            finally
            {
                if (!success)
                {
                    // Connection failed, clean everything up.
                    DisposeSerialObjeccts();
                }
            }

            return success;
        }

        /// <summary>
        /// Task executed periodically to communicate with device.
        /// </summary>
        private async void CommunicationLoop()
        {
            bool success = false;

            if (null == _dataReader || null == _serialDevice)
            {
                // Serial device disconnected while we were waiting in the dispatcher queue.
                return;
            }

            try
            {
                success = await PerformDeviceCommunication();

                if (success)
                {
                    _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, CommunicationLoop);
                }
            }
            catch (Exception e)
            {
                Log(e.ToString(), LoggingLevel.Error);
            }

            if (!success && _shouldReconnect)
            {
                Log($"{DeviceLabel} communication loop encountered error, try to reconnect.", LoggingLevel.Error);
                _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, Reconnect);
            }
        }

        /// <summary>
        /// Write the given string out to the serial device.
        /// </summary>
        /// <param name="value">String to write to device</param>
        /// <returns>Task to await write completion</returns>
        protected Task WriteAndStore(string value)
        {
            CancellationTokenSource cancelSrc = new CancellationTokenSource(WriteTaskCancelTimeout);

            _dataWriter.WriteString(value);
            return _dataWriter.StoreAsync().AsTask<uint>(cancelSrc.Token);
        }

        /// <summary>
        /// Load bytes from the serial device for later interpretation by ReaderReadString and others.
        /// </summary>
        /// <param name="count">Count of bytes to attempt reading</param>
        /// <returns>Number of bytes actually read</returns>
        protected Task<uint> ReaderLoadAsync(uint count)
        {
            CancellationTokenSource cancelSrc = new CancellationTokenSource(ReadTaskCancelTimeout);
            return _dataReader.LoadAsync((uint)count).AsTask<uint>(cancelSrc.Token);
        }

        /// <summary>
        /// Read bytes retrieved by ReaderLoadAsync, interpreting as string.
        /// </summary>
        /// <param name="codeUnitCount">Length of expected string.</param>
        /// <returns>String sent by serial device</returns>
        protected string ReaderReadString(uint codeUnitCount)
        {
            return _dataReader.ReadString(codeUnitCount);
        }

        /// <summary>
        /// Closes the serial device connection and attempt to reconnect.
        /// </summary>
        private async void Reconnect()
        {
            bool success = false;

            if (_shouldReconnect)
            {
                DisposeSerialObjeccts();
                Log($"Reconnecting to {DeviceLabel}", LoggingLevel.Information);
                try
                {
                    success = await Connect(_serialDeviceId);
                }
                catch (Exception e)
                {
                    Log($"Failed to reconnect to {DeviceLabel}", LoggingLevel.Information);
                    Log(e.ToString(), LoggingLevel.Information);
                }

                if (!success)
                {
                    await Task.Delay(RetryDelay);
                    _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, Reconnect);
                }
            }
        }

        /// <summary>
        /// Connect to serial device on given deviceId and see if it acts like the target device.
        /// </summary>
        /// <param name="deviceId">Device ID string from DeviceInformation.Id</param>
        /// <returns>True if the device behaves as expected</returns>
        public async Task<bool> IsDeviceOnPort(string deviceId)
        {
            bool success = false;

            Log($"Checking if this is a {DeviceLabel}: {deviceId}", LoggingLevel.Information);
            try
            {
                success = await Connect(deviceId);

                // In case of failure, Connect() is responsible for cleanup up connection.
                if (success)
                {
                    Disconnect();
                }
            }
            catch (Exception e)
            {
                // Since this is not during actual use, treat as informational rather than error.
                Log($"Exception thrown while checking if {DeviceLabel} is on {deviceId}", LoggingLevel.Information);
                Log(e.ToString(), LoggingLevel.Information);
            }

            return success;
        }

        /// <summary>
        /// Dispose all the serial communication objects associated with this device, do not try to reconnect.
        /// </summary>
        public virtual void Disconnect()
        {
            _shouldReconnect = false;
            DisposeSerialObjeccts();
        }

        /// <summary>
        /// Dispose all the serial communication objects associated with this device. We may or may not try to
        /// reconnect after this.
        /// </summary>
        public virtual void DisposeSerialObjeccts()
        {
            Log($"Disposing serial I/O objects of {DeviceLabel}");
            _dataWriter?.Dispose();
            _dataWriter = null;
            _dataReader?.Dispose();
            _dataReader = null;
            _serialDevice?.Dispose();
            _serialDevice = null;
        }

        //-----------------------------------------------------------------------------------------------------------
        //  Operational parameters that can be optionally overridden by derived classes.

        /// <summary>
        /// Given to the serial device so read operations can time out instead of stuck waiting for
        /// data that may never come.
        ///
        /// In case it doesn't work, backup is ReadTaskCancelTimeout, which should be a larger value.
        /// </summary>
        protected virtual int ReadTimeout
        {
            get
            {
                return 500;
            }
        }

        /// <summary>
        /// How long to wait before cancelling async I/O, in milliseconds. When a read operation fails
        /// to return after this amount of time, a TaskCanceledException is thrown instead of risking
        /// being stuck forever.
        ///
        /// Since this is a backup for serial port read timeout, it should be a larger value.
        /// </summary>
        protected virtual int ReadTaskCancelTimeout
        {
            get
            {
                return 2000;
            }
        }

        /// <summary>
        /// Given to the serial device so write operations can time out instead of stuck waiting for
        /// data that may never come.
        ///
        /// In case it doesn't work, backup is WriteTaskCancelTimeout, which should be a larger value.
        /// </summary>
        protected virtual int WriteTimeout
        {
            get
            {
                return 100;
            }
        }

        /// <summary>
        /// How long to wait before cancelling async I/O, in milliseconds. When a write operation fails
        /// to return after this amount of time, a TaskCanceledException is thrown instead of risking
        /// being stuck forever.
        ///
        /// Since this is a backup for serial port write timeout, it should be a larger value.
        /// </summary>
        protected virtual int WriteTaskCancelTimeout
        {
            get
            {
                return 1000;
            }
        }

        /// <summary>
        /// How long to wait before retrying connection, in milliseconds
        /// </summary>
        protected virtual int RetryDelay
        {
            get
            {
                return 5000;
            }
        }

        //-----------------------------------------------------------------------------------------------------------
        //  Operational parameters that must be overridden by derived classes.

        /// <summary>
        /// String for user-readable identification DeviceLabel in error messages and logs
        /// </summary>
        protected abstract string DeviceLabel
        {
            get;
        }

        protected abstract uint               DeviceBaudRate { get; }
        protected abstract ushort             DeviceDataBits { get; }
        protected abstract SerialParity       DeviceParity { get; }
        protected abstract SerialStopBitCount DeviceStopBits { get; }

        /// <summary>
        /// Test device communication by retrieving a piece of sample data
        /// </summary>
        /// <returns>True if valid data was retrieved.</returns>
        protected abstract Task<bool> GetSampleData();

        /// <summary>
        /// Returns the next piece of data from the device
        /// </summary>
        /// <returns>String containing data sent by device</returns>
        protected abstract Task<string> NextDataString();

        /// <summary>
        /// This is called regularly so derived classes can perform their own device-specific communication tasks.
        ///
        /// Should call await Task.Delay() an amount appropriate for the device before returning. Without the
        /// delay, it may starve peer peripherals of processing cycles for their own communication tasks, but
        /// the delay may be skipped if necessary for brief bursts of maximum performance.
        ///
        /// In case of error, either return "false" or throw an exception. In both cases, the serial connection will
        /// be closed and a reconnection to the device will be attempted after waiting "RetryDelay"
        /// </summary>
        /// <returns>True if successful, False if connection should be closed and reconnected.</returns>
        protected abstract Task<bool> PerformDeviceCommunication();

    }
}
