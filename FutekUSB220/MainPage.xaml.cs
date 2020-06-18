using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Diagnostics;
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
        private DispatcherTimer activityUpdateTimer;
        private Logger logger = null;

        public MainPage()
        {
            this.InitializeComponent();
            activityUpdateTimer = new DispatcherTimer();
            activityUpdateTimer.Tick += ActivityUpdateTimer_Tick;
            activityUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, 250 /* milliseconds */);
            activityUpdateTimer.Start();

            if (Application.Current as App != null)
            {
                logger = ((App)Application.Current).logger;
            }

            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, EnumerateSerialDevices);
        }
        private void ActivityUpdateTimer_Tick(object sender, object e)
        {
            tbLogging.Text = logger.Recent;
            tbClock.Text = DateTime.UtcNow.ToString("yyyyMMddHHmmssff");
        }

        private void Log(string t, LoggingLevel level = LoggingLevel.Verbose)
        {
            if (logger != null)
            {
                logger.Log(t, level);
            }
            else
            {
                Debug.WriteLine("WARNING: Logger not available, log message lost.");
            }
        }

        private async void EnumerateSerialDevices()
        {
            DeviceInformationCollection deviceinfos = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());

            foreach (DeviceInformation deviceinfo in deviceinfos)
            {
                Log($"Serial device ID={deviceinfo.Id}");
                try
                {
                    using (SerialDevice device = await SerialDevice.FromIdAsync(deviceinfo.Id))
                    {
                        if (device != null)
                        {
                            device.BaudRate = 9600;
                            device.DataBits = 8;
                            device.Parity = SerialParity.None;
                            device.StopBits = SerialStopBitCount.One;
                            device.ReadTimeout = new TimeSpan(0, 0, 1);
                            device.WriteTimeout = new TimeSpan(0, 0, 2);

                            using (DataReader reader = new DataReader(device.InputStream))
                            {
                                CancellationTokenSource cancelSrc = new CancellationTokenSource(2000); // Cancel after 2000 milliseconds

                                reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                                reader.InputStreamOptions = InputStreamOptions.ReadAhead;

                                Log("Small serial read expecting repeated readings");
                                uint loadedSize = await reader.LoadAsync(18).AsTask<uint>(cancelSrc.Token);
                                Log($"reader.LoadAsync returned {loadedSize} bytes");
                                if (loadedSize == 18)
                                {
                                    string helloText = reader.ReadString(loadedSize);
                                    Log($"Hello text = {helloText}");
                                    // Verify expected format for load cell reading
                                    if ((helloText.StartsWith('+') || helloText.StartsWith('-')) &&
                                         helloText.EndsWith("lbs\r\n"))
                                    {
                                        double value = Double.Parse(helloText.Substring(0, 13));
                                        Log($"Parsed value={value}");
                                        // If we get this far, looks like a load cell to us!
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log(e.ToString(), LoggingLevel.Error);
                }
            }
        }
    }
}
