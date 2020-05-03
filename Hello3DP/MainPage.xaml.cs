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
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Hello3DP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private static TextBox outputbox = null;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void Log(string t, LoggingLevel level=LoggingLevel.Verbose)
        {
            Logger logger = ((App)Application.Current).logger;
            if (logger != null)
            {
                logger.Log(t, level);
            }
            else
            {
                Debug.WriteLine("WARNING: Logger not available, log message lost.");
            }
        }

        private static void AppendText(string t)
        {
            if (outputbox != null)
            {
                outputbox.Text += "\r\n";
                outputbox.Text += t;
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (outputbox == null)
            {
                outputbox = textOutput;
            }
            Log("MainPage.OnNavigatedTo", LoggingLevel.Information);
            await EnumerateDevices();
        }

        private async Task<int> EnumerateDevices()
        {
            Log("MainPage.EnumerateDevices", LoggingLevel.Information);

            DeviceInformationCollection deviceinfos = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());

            foreach (DeviceInformation deviceinfo in deviceinfos)
            {
                Log($"Trying DeviceInformation with name={deviceinfo.Name} ID={deviceinfo.Id}");

                try
                {
                    using (SerialDevice device = await SerialDevice.FromIdAsync(deviceinfo.Id))
                    {
                        if (device != null)
                        {
                            device.BaudRate = 250000;
                            device.DataBits = 8;
                            device.StopBits = SerialStopBitCount.One;
                            device.Parity = SerialParity.None;
                            device.ReadTimeout = new TimeSpan(0, 0, 1);
                            device.WriteTimeout = new TimeSpan(0, 0, 2);

                            device.IsDataTerminalReadyEnabled = true; // Default is false, apparently required to be true to talk to RAMPS board.

                            using (DataReader reader = new DataReader(device.InputStream))
                            using (DataWriter writer = new DataWriter(device.OutputStream))
                            {
                                reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                                reader.InputStreamOptions = InputStreamOptions.ReadAhead;

                                Log("Send M115");
                                writer.UnicodeEncoding = UnicodeEncoding.Utf8;
                                writer.WriteString("M115\n");
                                await writer.StoreAsync();

                                Log("2K serial read expecting hello text");
                                await reader.LoadAsync(2048);
                                Log($"Retrieved buffer of size {reader.UnconsumedBufferLength}");
                                string helloText = reader.ReadString(reader.UnconsumedBufferLength);
                                Log(helloText);

                                device.ReadTimeout = new TimeSpan(0, 0, 5);
                                Log("2K serial read expecting SD init");
                                await reader.LoadAsync(2048);
                                Log($"Retrieved buffer of {reader.UnconsumedBufferLength}");
                                Log(reader.ReadString(reader.UnconsumedBufferLength));
                            }
                        }
                        else
                        {
                            Log("Device was null, no action taken.");
                        }
                    }
                }
                catch (Exception e)
                {
                    Log(e.ToString());
                    Log(e.Message);
                    Log(e.StackTrace);
                }
            }

            return deviceinfos.Count;
        }
    }
}
