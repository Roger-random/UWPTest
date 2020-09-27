using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
using Windows.UI.Core;
using UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding;

using Com.Regorlas.Logging;

namespace Com.Regorlas.Serial
{
    class MVCEventArgs : EventArgs
    {
        public MVCEventArgs(string response)
        {
            Response = response;
        }

        public string Response { get; }
    }

    class TestStub : SerialPeripheralBase
    {
        private const string DELIMITER = "\r\n";
        private int _loopDelay;

        // -------------------------------------------------------------------------------------------------------
        //
        //  Abstract base properties we must implement
        //
        protected override string DeviceLabel { get { return "test stub"; } }
        protected override uint DeviceBaudRate { get { return 9600; } }
        protected override ushort DeviceDataBits { get { return 8; } }
        protected override SerialParity DeviceParity { get { return SerialParity.None; } }
        protected override SerialStopBitCount DeviceStopBits { get { return SerialStopBitCount.One; } }

        // -------------------------------------------------------------------------------------------------------
        //
        // Event signaling a data update
        //

        public delegate void MVCEventHandler(object sender, MVCEventArgs args);
        public event MVCEventHandler MVCEvent;

        private void OnMVCEvent(MVCEventArgs e)
        {
            MVCEventHandler raiseEvent = MVCEvent;

            if (raiseEvent != null)
            {
                raiseEvent(this, e);
            }
        }

        public TestStub(CoreDispatcher dispatcher, Logger logger) : base(dispatcher, logger)
        {
            _loopDelay = 250;
        }

        protected override async Task<bool> GetSampleData()
        {
            bool noDataStreaming = false;
            string sampleData;
            bool success = false;

            try
            {
                sampleData = await NextDataString();
                Log($"Expected no data but retrieved {sampleData}, this is not {DeviceLabel}");
                noDataStreaming = false;
            }
            catch (TaskCanceledException)
            {
                // We timed out trying to read data, which is expected and desirable because this
                // type of device does not send data until command is sent.
                noDataStreaming = true;
            }
            catch (IOException)
            {
                // An IO error, however, is a failure condition and probably means we're not
                // looking at the right port.
                Log($"Encountered IO error, implying this is not {DeviceLabel}");
            }

            if (noDataStreaming)
            {
                // We verified this device is silent when unprompted. Now we send a prompt to
                // see if it answers.
                await WriteAndStore("VER\r\n");

                try
                {
                    sampleData = await NextDataString();

                    if (sampleData == "VER TESTSTUB 1.0\r\n")
                    {
                        Log($"Saw response valid for a {DeviceLabel}");
                        success = true;
                    }
                    else
                    {
                        Log($"Saw response but not valid for a {DeviceLabel}");
                    }
                }
                catch (TaskCanceledException)
                {
                    Log($"Timed out after command, this is not {DeviceLabel}");
                }
            }

            return success;
        }

        protected override async Task<bool> PerformDeviceCommunication()
        {
            // Send the query...
            await WriteAndStore("MVC 1 2\r\n");

            // .. read the response and pass it on to subscribers
            string newValue = await NextDataString();

            OnMVCEvent(new MVCEventArgs(newValue));

            // Take a short break before continuing the read loop
            await Task.Delay(_loopDelay);

            return true;
        }

        protected override async Task<string> NextDataString()
        {
            string inString = null;

            uint loadedSize = await ReaderLoadAsync(1024);
            if (loadedSize > 0)
            {
                try
                {
                    inString = ReaderReadString(loadedSize);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // This is thrown when the data can't be parsed as UTF8, interpreting to mean the incoming
                    // data was not sent at the baud rate of the expected device.
                    IOError($"Non UTF-8 data encountered. This is expected when {DeviceLabel} is not on this port.");
                }

                if (!inString.EndsWith(DELIMITER))
                {
                    IOError($"{DeviceLabel} response did not end with expected delimiter.");
                }
            }
            else
            {
                IOError("Unexpected: LoadAsync() returned with zero bytes.");
            }

            return inString;
        }
    }
}
