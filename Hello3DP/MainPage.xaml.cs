using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Foundation.Collections;
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
            AppendText("OnNavigatedTo");
            await EnumerateDevices();
        }

        private async Task<int> EnumerateDevices()
        {
            AppendText("Enumerating...");

            DeviceInformationCollection deviceinfos = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());

            foreach (DeviceInformation deviceinfo in deviceinfos)
            {
                AppendText($"Looking at Name={deviceinfo.Name} ID={deviceinfo.Id}");

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

                            device.ReadTimeout = new TimeSpan(0, 0, 3);

                            using (DataReader reader = new DataReader(device.InputStream))
                            {
                                reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                                reader.InputStreamOptions = InputStreamOptions.ReadAhead;

                                Boolean readmore = true;
                                while(readmore)
                                {
                                    await reader.LoadAsync(2048);
                                    if (reader.UnconsumedBufferLength < 2048)
                                    {
                                        readmore = false;
                                    }

                                    AppendText(reader.ReadString(reader.UnconsumedBufferLength));
                                }
                                AppendText("\r\n\r\n- -\r\nRead complete.");
                            }
                        }
                        else
                        {
                            AppendText("Device was null, no action taken.");
                        }
                    }
                }
                catch (Exception e)
                {
                    AppendText(e.ToString());
                }
            }

            return deviceinfos.Count;
        }
    }
}
