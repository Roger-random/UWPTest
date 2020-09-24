﻿using System;
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
                _serialDevice.ReadTimeout = new TimeSpan(0, 0, 0, 0, TaskCancelTimeout /* milliseconds */);

                _dataReader = new DataReader(_serialDevice.InputStream);
                _dataReader.UnicodeEncoding = UnicodeEncoding.Utf8;

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
            string sampleData = null;
            bool connected = false;

            try
            {
                if (await SetupSerialDevice(deviceId))
                {
                    sampleData = await NextDataString();
                    Log("Successfully retrieved sample data."); // {sampleData} content unpredictable, do not log.

                    connected = true;
                    _shouldReconnect = true;

                    // Kick off the communication loop
                    _ = _dispatcher.RunAsync(CoreDispatcherPriority.Low, CommunicationLoop);
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
                if (!connected)
                {
                    // Connection failed, clean everything up.
                    Disconnect();
                }
            }

            return connected;
        }

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

                _ = _dispatcher.RunAsync(CoreDispatcherPriority.Normal, CommunicationLoop);
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

        protected abstract Task<bool> PerformDeviceCommunication();

        protected Task<uint> ReaderLoadAsync(uint count)
        {
            CancellationTokenSource cancelSrc = new CancellationTokenSource(TaskCancelTimeout);
            return _dataReader.LoadAsync((uint)count).AsTask<uint>(cancelSrc.Token);
        }

        protected string ReaderReadString(uint codeUnitCount)
        {
            return _dataReader.ReadString(codeUnitCount);
        }

        private async void Reconnect()
        {
            bool success = false;

            DisposeSerialObjeccts();
            if (_shouldReconnect)
            {
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

        // Connect to serial device on given deviceId and see if it acts like the target device
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

        public virtual void Disconnect()
        {
            _shouldReconnect = false;
            DisposeSerialObjeccts();
        }

        public virtual void DisposeSerialObjeccts()
        {
            Log($"Disconnecting {DeviceLabel}");
            _dataReader?.Dispose();
            _dataReader = null;
            _serialDevice?.Dispose();
            _serialDevice = null;
        }

        //-----------------------------------------------------------------------------------------------------------
        //  Operational parameters that can be optionally overridden by derived classes.

        /// <summary>
        /// How long to wait before cancelling async I/O, in milliseconds
        /// </summary>
        protected virtual int TaskCancelTimeout
        {
            get
            {
                return 2000;
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
        /// Returns the next piece of data from the device
        /// </summary>
        /// <returns>String containing data sent by device</returns>
        protected abstract Task<string> NextDataString();
    }
}