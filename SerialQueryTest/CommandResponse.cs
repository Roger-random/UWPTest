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

using Com.Regorlas.Logging;
using System.Reflection.Metadata.Ecma335;

namespace Com.Regorlas.Serial
{
    class ResponseDataEventArgs : EventArgs
    {
        public ResponseDataEventArgs(double value)
        {
            Value = value;
        }

        public double Value { get; }
    }

    // Test class representing serial devices that continuously emit data
    class CommandResponse : SerialPeripheralBase
    {
        // How long to wait between querying position. If this is too short, it
        // appears to starve some shared resources and will affect other serial ports.
        private const int LOOP_DELAY = 100;

        // Expected data format: "+00.00000\r"
        private const int EXPECTED_LENGTH = 10;
        private const string DELIMITER = "\r";

        // -------------------------------------------------------------------------------------------------------
        //
        //  Abstract base properties we must implement
        //
        protected override string DeviceLabel { get { return "command+response device"; } }
        protected override uint DeviceBaudRate { get { return 4800; } }
        protected override ushort DeviceDataBits { get { return 7; } }
        protected override SerialParity DeviceParity { get { return SerialParity.Even; } }
        protected override SerialStopBitCount DeviceStopBits { get { return SerialStopBitCount.Two; } }

        // -------------------------------------------------------------------------------------------------------
        //
        // Event signaling a data update
        //

        public delegate void ResponseDataEventHandler(object sender, ResponseDataEventArgs args);
        public event ResponseDataEventHandler ResponseDataEvent;

        public CommandResponse(CoreDispatcher dispatcher, Logger logger) : base (dispatcher, logger)
        {
        }
        protected virtual void OnResponseDataEvent(ResponseDataEventArgs e)
        {
            ResponseDataEventHandler raiseEvent = ResponseDataEvent;

            if (raiseEvent != null)
            {
                raiseEvent(this, e);
            }
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
                Log($"Encountered IO error, implying this is not {DeviceLabel}");
            }

            if (noDataStreaming)
            {
                // We verified this device is silent when unprompted. Now we send a prompt to
                // see if it answers.
                await WriteAndStore("?");

                try
                {
                    sampleData = await NextDataString();
                    Log($"Saw response valid for a {DeviceLabel}");
                    success = true;
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
            await WriteAndStore("?");

            // .. read the response and pass it on to subscribers
            string newValue = await NextDataString();
            double parsedValue = Double.Parse(newValue);
            OnResponseDataEvent(new ResponseDataEventArgs(parsedValue));

            // Take a short break before continuing the read loop
            await Task.Delay(LOOP_DELAY);

            return true;
        }

        private bool ValidateFormat(string inputData)
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

        protected override async Task<string> NextDataString()
        {
            string inString = null;

            uint loadedSize = await ReaderLoadAsync(EXPECTED_LENGTH);
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
                    IOError($"{DeviceLabel} response did not end with expected deliminiter.");
                }

                if (!ValidateFormat(inString))
                {
                    IOError($"Improper format string {inString}");
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
