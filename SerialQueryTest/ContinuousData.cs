using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
using Windows.Foundation.Diagnostics;
using Windows.UI.Core;

using Com.Regorlas.Logging;

namespace Com.Regorlas.Serial
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
    class ContinuousData : SerialPeripheralBase
    {
        // How long to wait between querying position. If this is too short, it
        // appears to starve some shared resources and will affect other serial ports.
        private const int LOOP_DELAY = 50;

        // Expected format: "+0.00000     lbs\r\n"
        private const int EXPECTED_LENGTH = 18;
        private const int DATA_LENGTH = 13; // When parsing as double, stop just before this character index.
        private const string DELIMITER = "\r\n";

        private uint _lengthMultiplier;

        // -------------------------------------------------------------------------------------------------------
        //
        //  Abstract base properties we must implement
        //
        protected override string DeviceLabel { get { return "continuous data device"; } }
        protected override uint DeviceBaudRate { get { return 9600; } }
        protected override ushort DeviceDataBits { get { return 8; } }
        protected override SerialParity DeviceParity { get { return SerialParity.None; } }
        protected override SerialStopBitCount DeviceStopBits { get { return SerialStopBitCount.One; } }

        // -------------------------------------------------------------------------------------------------------
        //
        // Event signaling a data update
        //

        public delegate void ContinuousDataEventHandler(object sender, ContinuousDataEventArgs args);
        public event ContinuousDataEventHandler ContinousDataEvent;

        public ContinuousData(CoreDispatcher dispatcher, Logger logger) : base (dispatcher, logger)
        {
            _lengthMultiplier = 1;
        }
        protected virtual void OnContinuousDataEvent(ContinuousDataEventArgs e)
        {
            ContinuousDataEventHandler raiseEvent = ContinousDataEvent;

            if (raiseEvent != null)
            {
                raiseEvent(this, e);
            }
        }

        protected override async Task<bool> GetSampleData()
        {
            // For initial load, we have more patience and grab just one piece of data.
            UpdateReadWriteTimeouts(500, 100);
            _lengthMultiplier = 1;

            string sampleData = await NextDataString();
            Log("Successfully retrieved sample data."); // {sampleData} content unpredictable, do not log.

            // Once successfully loaded the first piece of data, less patience and grab more.
            UpdateReadWriteTimeouts(20, 100);
            _lengthMultiplier = 10;

            return true;
        }

        protected override async Task<bool> PerformDeviceCommunication()
        {
            // Read the next value
            string dataString = await NextDataString();
            double parsedValue = Double.Parse(dataString.Substring(0, DATA_LENGTH));

            // And send it to subscribers
            OnContinuousDataEvent(new ContinuousDataEventArgs(parsedValue));

            // Take a short break before continuing the read loop
            await Task.Delay(LOOP_DELAY);

            // Simple implementation always returns "true" for successful.
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
            return Regex.IsMatch(inputData, "^[+-]\\d+.\\d+\\s+lbs\r\n$");
        }

        protected override async Task<string> NextDataString()
        {
            string inString = null;
            int deliminiterIndex;

            uint loadedSize = await ReaderLoadAsync(EXPECTED_LENGTH*_lengthMultiplier);
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

                deliminiterIndex = inString.LastIndexOf(DELIMITER);
                if (deliminiterIndex < (EXPECTED_LENGTH - DELIMITER.Length))
                {
                    IOError($"Failed to read enough characters from {DeviceLabel}");
                }
                else if(deliminiterIndex > (EXPECTED_LENGTH-DELIMITER.Length))
                {
                    // We have read more than one string of the expected format, extract just the final
                    // instance.
                    inString = inString.Substring(deliminiterIndex - (EXPECTED_LENGTH - DELIMITER.Length), EXPECTED_LENGTH);
                    Log($"Discarding {loadedSize - EXPECTED_LENGTH} from {DeviceLabel}");
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
