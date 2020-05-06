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
        private DispatcherTimer activityPanelUpdateTimer;

        public MainPage()
        {
            this.InitializeComponent();

            controller = new XYZControl();

            activityPanelUpdateTimer = new DispatcherTimer();
            activityPanelUpdateTimer.Tick += activityPanelUpdate;
            activityPanelUpdateTimer.Interval = new TimeSpan(0, 0, 1);
            activityPanelUpdateTimer.Start();
        }

        private void activityPanelUpdate(object sender, object e)
        {
            activity.Text = ((App)Application.Current).logger.Recent;
            tbUTCNow.Text = DateTime.UtcNow.ToString("yyyyMMddHHmmssff");

            if (controller != null)
            {
                if (!controller.IsOpen)
                {
                    connectBtn.Content = "Connect";
                }
            }

            tbPosition.Text = $"X: {controller.X,8:N2} Y: {controller.Y,8:N2} Z: {controller.Z,8:N2}";
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
                Log("Disconnecting", LoggingLevel.Information);
                controller.Close();
            }
            else
            {
                DeviceInformationCollection deviceinfos = await DeviceInformation.FindAllAsync(SerialDevice.GetDeviceSelector());
                bool deviceConnected = false;

                Log($"Number of serial devices available = {deviceinfos.Count}", LoggingLevel.Information);
                foreach (DeviceInformation deviceinfo in deviceinfos)
                {
                    deviceConnected = await controller.Open(deviceinfo);
                    if (deviceConnected)
                    {
                        controller.BeginReadAsync(Dispatcher);
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

        private void homeBtn_Click(object sender, RoutedEventArgs e)
        {
            controller.Home();
        }

        private void getPosBtn_Click(object sender, RoutedEventArgs e)
        {
            controller.GetPos();
        }

        private void middishBtn_Click(object sender, RoutedEventArgs e)
        {
            controller.MiddleIsh();
        }
    }
}
