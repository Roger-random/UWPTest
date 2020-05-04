using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace PollingComms
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private XYZControl controller;

        public MainPage()
        {
            this.InitializeComponent();
            controller = new XYZControl();
        }

        private void Log(string t, LoggingLevel level = LoggingLevel.Verbose)
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

        private async void connectBtn_Click(object sender, RoutedEventArgs e)
        {
            if (controller.IsOpen)
            {
                Log("Disconnecting");
                controller.Close();
            }
            else
            {
                DeviceInformationCollection deviceinfos = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());
                bool deviceConnected = false;

                Log("Attempting to connect...");
                foreach (DeviceInformation deviceinfo in deviceinfos)
                {
                    deviceConnected = await controller.Open(deviceinfo);
                    if (deviceConnected)
                    {
                        Log("Connection successful");
                        break;
                    }
                }
            }

            if (controller.IsOpen)
            {
                connectBtn.Content = "Disconnect";
            }
            else
            {
                connectBtn.Content = "Connect";
            }
        }
    }
}
