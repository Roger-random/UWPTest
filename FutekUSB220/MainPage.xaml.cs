using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Diagnostics;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace FutekUSB220
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string FutekUSB220DeviceId = "FutekUSB220";

        private DispatcherTimer activityUpdateTimer;
        private Logger logger = null;

        private SerialDevice _serialDevice = null;
        private DataReader _dataReader = null;

        public double SensorValue { get; private set; } = 0;

        public MainPage()
        {
            this.InitializeComponent();
            activityUpdateTimer = new DispatcherTimer();
            activityUpdateTimer.Tick += ActivityUpdateTimer_Tick;
            activityUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, 200 /* milliseconds */);
            activityUpdateTimer.Start();

            if (Application.Current as App != null)
            {
                logger = ((App)Application.Current).logger;
            }

            Application.Current.Suspending += Application_Suspending;
            Application.Current.Resuming += Application_Resuming;

            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, EnumerateSerialDevices);
        }

        private async void Application_Resuming(object sender, object e)
        {
            await Connect((string)ApplicationData.Current.LocalSettings.Values[FutekUSB220DeviceId]);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, UpdateSensorValue);
        }

        private void Application_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            Disconnect();
        }

        private void ActivityUpdateTimer_Tick(object sender, object e)
        {
            tbLogging.Text = logger.Recent;
            tbClock.Text = DateTime.UtcNow.ToString("yyyyMMddHHmmssff");
            tbSensorValue.Text = $"{SensorValue,9:f4} lbs";
        }

        private async void Log(string t, LoggingLevel level = LoggingLevel.Verbose)
        {
            if (logger != null)
            {
                logger.Log(t, level);
                if (level > LoggingLevel.Information)
                {
                    await logger.WriteLogBlock();
                }
            }
            else
            {
                Debug.WriteLine("WARNING: Logger not available, log message lost.");
            }
        }

        private async Task<double> ReadSensorReport()
        {
            double value = 0;

            if (_serialDevice == null)
            {
                InvalidOperation("ReadSensorReport called before connecting serial device.");
            }
            if (_dataReader == null)
            {
                InvalidOperation("ReadSensorReport called before connecting data reader.");
            }

            CancellationTokenSource cancelSrc = new CancellationTokenSource(2000); // Cancel after 2000 milliseconds

            uint loadedSize = await _dataReader.LoadAsync(18).AsTask<uint>(cancelSrc.Token);
            if (loadedSize == 18)
            {
                string reportLine = _dataReader.ReadString(18);

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
                if (reportLine.Length == 18 && Regex.IsMatch(reportLine, "^[+-]\\d+.\\d+\\s+lbs\r\n$"))
                {
                    value = Double.Parse(reportLine.Substring(0, 13));
                }
                else
                {
                    IOError($"Expected length 18 actual {reportLine.Length}. String={reportLine}");
                }
            }
            else
            {
                IOError($"DataReader read {loadedSize} bytes instead of the requested 18.");
            }

            return value;
        }
        private async void UpdateSensorValue()
        {
            try
            {
                SensorValue = await ReadSensorReport();

                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, UpdateSensorValue);
            }
            catch (Exception e)
            {
                Log($"UpdateSensorValue will not requeue itself due to {e.ToString()}", LoggingLevel.Error);
            }
        }


        private void IOError(string message)
        {
            Log(message, LoggingLevel.Error);
            throw new IOException(message);
        }

        private void InvalidOperation(string message)
        {
            Log(message, LoggingLevel.Error);
            throw new InvalidOperationException(message);
        }

        private bool Connected()
        {
            return _serialDevice != null && _dataReader != null;
        }

        private async Task<bool> Connect(string deviceId)
        {
            try
            {
                Log($"Check if Futek USB220 is at {deviceId}");
                _serialDevice = await SerialDevice.FromIdAsync(deviceId);
                if (_serialDevice != null)
                {
                    _serialDevice.BaudRate = 9600;
                    _serialDevice.DataBits = 8;
                    _serialDevice.Parity = SerialParity.None;
                    _serialDevice.StopBits = SerialStopBitCount.One;
                    _serialDevice.ReadTimeout = new TimeSpan(0, 0, 1);
                    _serialDevice.WriteTimeout = new TimeSpan(0, 0, 2);

                    _dataReader = new DataReader(_serialDevice.InputStream);
                    _dataReader.UnicodeEncoding = UnicodeEncoding.Utf8;

                    double initialValue = await ReadSensorReport();
                    Log($"Successfully read an initial value of {initialValue} lbs.");

                    ApplicationData.Current.LocalSettings.Values[FutekUSB220DeviceId] = deviceId;

                    return true;
                }
                Log($"Failed to acquire SerialDevice from {deviceId}");
            }
            catch (Exception e)
            {
                Log($"Probably not a Futek USB220 at {deviceId}");
                Log($"Due to error encountered: {e.ToString()}");
                Disconnect();
            }

            return false;
        }

        private void Disconnect()
        {
            Log("Disconnect Futek USB220");
            _dataReader?.Dispose();
            _dataReader = null;
            _serialDevice?.Dispose();
            _serialDevice = null;
        }

        private async void EnumerateSerialDevices()
        {
            DeviceInformationCollection deviceinfos = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());

            foreach (DeviceInformation deviceinfo in deviceinfos)
            {
                Log($"Serial device ID={deviceinfo.Id}");
                if (await Connect(deviceinfo.Id))
                {
                    await Dispatcher.RunAsync(CoreDispatcherPriority.Low, UpdateSensorValue);
                    break;
                }
            }
        }
    }
}
