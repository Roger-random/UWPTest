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

                            device.IsDataTerminalReadyEnabled = true; // Default is false, apparently required to be true to talk to RAMPS board.

                            using (DataReader reader = new DataReader(device.InputStream))
                            {
                                reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                                reader.InputStreamOptions = InputStreamOptions.ReadAhead;

                                Boolean readmore = true;
                                int readIndex = -1;
                                Task[] readtask = new Task[1];
                                CancellationTokenSource readCancelSrc = new CancellationTokenSource();
                                while(readmore)
                                {
                                    Log("Reading serial input buffer asking for 2K with 1 second timeout.");
                                    readtask[0] = reader.LoadAsync(2048).AsTask(readCancelSrc.Token);
                                    readIndex = Task.WaitAny(readtask, 1000);
                                    if (readIndex == -1)
                                    {
                                        readmore = false;
                                        readCancelSrc.Cancel();
                                        Log("No serial data for 1 second.");
                                    }
                                    else
                                    {
                                        string serialdata = reader.ReadString(reader.UnconsumedBufferLength);
                                        Log($"Retrieved {serialdata.Length} chars. Sample: {serialdata}");
                                    }
                                }
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
                }
            }

            return deviceinfos.Count;
        }
    }
}
