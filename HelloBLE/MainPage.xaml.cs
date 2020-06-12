﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
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

namespace HelloBLE
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private DispatcherTimer activityUpdateTimer;
        private Logger logger = null;
        private bool tryConnecting = false;
        private bool connected = false;
        private BluetoothLEDevice device = null;

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

        private void btnBLEAdWatch_Click(object sender, RoutedEventArgs e)
        {
            BluetoothLEAdvertisementWatcher watcher = new BluetoothLEAdvertisementWatcher();
            Log("Advertisement Watcher created");
            watcher.Received += Watcher_Received;
            watcher.Start();
            Log("Advertisement Watcher started");
        }

        private async void Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            BluetoothLEAdvertisement advertisement = args.Advertisement;

            if (tryConnecting)
            {
                Log("Ignoring advertisement, connection attempt already underway.");
                return;
            }

            if (advertisement.LocalName == "SY289")
            {
                tryConnecting = true;
                Log($"SY289 heard at Bluetooth address {args.BluetoothAddress:x}");
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
                connected = true;
                Log($"Device with ID {device.DeviceId} acquired.");
                GattDeviceServicesResult getGattServices = await device.GetGattServicesAsync();
                if (GattCommunicationStatus.Success == getGattServices.Status)
                {
                    foreach (GattDeviceService service in getGattServices.Services)
                    {
                        Log($"  Service {service.Uuid}");
                        GattCharacteristicsResult getCharacteristics = await service.GetCharacteristicsAsync();
                        if (GattCommunicationStatus.Success == getCharacteristics.Status)
                        {
                            foreach (GattCharacteristic characteristic in getCharacteristics.Characteristics)
                            {
                                Log($"    Characteristic {characteristic.Uuid} property {characteristic.CharacteristicProperties}");
                                GattDescriptorsResult getDescriptors = await characteristic.GetDescriptorsAsync();
                                if (GattCommunicationStatus.Success == getDescriptors.Status)
                                {
                                    foreach (GattDescriptor descriptor in getDescriptors.Descriptors)
                                    {
                                        GattReadResult readResult = await descriptor.ReadValueAsync();
                                        DataReader dr = DataReader.FromBuffer(readResult.Value);
                                        Byte[] dataArray = new Byte[readResult.Value.Capacity];
                                        dr.ReadBytes(dataArray);
                                        string dataDump = $"      Descriptor {descriptor.Uuid} value 0x";
                                        foreach (Byte d in dataArray)
                                        {
                                            dataDump += $"{d:x2}";
                                        }
                                        Log(dataDump);
                                    }
                                }
                                else
                                {
                                    Log($"Getting GATT descriptors failed with status {getDescriptors.Status}");
                                }
                            }
                        }
                        else
                        {
                            Log($"Getting GATT characteristics failed with status {getCharacteristics.Status}");
                        }
                    }
                }
                else
                {
                    Log($"Getting GATT services failed with status {getGattServices.Status}");
                }

            }
            else
            {
                Log($"Ignoring BLE advertisement from device (not Sylvac indicator) {advertisement.LocalName}");
            }
        }
    }
}
