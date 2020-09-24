using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.SerialCommunication;
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
        }
        protected virtual void OnContinuousDataEvent(ContinuousDataEventArgs e)
        {
            ContinuousDataEventHandler raiseEvent = ContinousDataEvent;

            if (raiseEvent != null)
            {
                raiseEvent(this, e);
            }
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
            int syncRemainder = 0;

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
                        IOError($"No deliminter in data retrieved from {DeviceLabel}");
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

                    loadedSize = await ReaderLoadAsync((uint)syncRemainder);
                    if (loadedSize != syncRemainder)
                    {
                        // If we are really just picking up a few straggler bytes, this would be easy.
                        // If it is a problem, something else is wrong.
                        IOError($"Failed to resync, want {syncRemainder} but got {loadedSize}");
                    }

                    // Not checking for ArgumentOutOfRangeException here because if baud rate is wrong we should
                    // have already triggered ArgumentOutOfRangeException in the earlier ReadString().
                    inString += ReaderReadString(loadedSize);

                    if ((EXPECTED_LENGTH-DELIMITER.Length) != inString.IndexOf(DELIMITER))
                    {
                        IOError($"Failed to resync, only got {inString.Length} long string: {inString}");
                    }
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
