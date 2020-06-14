using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Diagnostics;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace SylvacMarkVI
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private const string BluetoothIDKey = "BluetoothID";
        private const string IndicatorName = "SY289";
        private Guid BTSIG_BatteryService = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
        private Guid BTSIG_BatteryLevel = new Guid("00002a19-0000-1000-8000-00805f9b34fb");
        private GattCharacteristic batteryLevelCharacteristic = null;
        private BluetoothLEAdvertisementWatcher watcher = null;
        private BluetoothLEDevice device = null;

        private DispatcherTimer activityUpdateTimer;
        private Logger logger = null;

        public MainPage()
        {
            this.InitializeComponent();

            // Start timer that periodically updates the diagnostics text at the bottom of the window
            activityUpdateTimer = new DispatcherTimer();
            activityUpdateTimer.Tick += ActivityUpdateTimer_Tick;
            activityUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, 250 /* milliseconds */);
            activityUpdateTimer.Start();

            // For convenience, save a pointer to logger module
            if (Application.Current as App != null)
            {
                logger = ((App)Application.Current).logger;
            }

            // Queue task to connect to device via BLE
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, DeviceConnect);
        }

        private async void DeviceConnect()
        {
            if (device != null)
            {
                Log("DeviceConnect should not be called when device is already connected", LoggingLevel.Warning);
            }
            else
            {
                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                if (localSettings.Values.ContainsKey(BluetoothIDKey))
                {
                    ulong bluetoothId = (ulong)localSettings.Values[BluetoothIDKey];
                    device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothId);
                    if (device != null)
                    {
                        Log($"Successfully acquired BluetoothLEDevice {device.Name}");

                        GattDeviceServicesResult getServices = await device.GetGattServicesForUuidAsync(BTSIG_BatteryService);
                        if (GattCommunicationStatus.Success == getServices.Status)
                        {
                            GattCharacteristicsResult getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_BatteryLevel);
                            if (GattCommunicationStatus.Success == getCharacteristics.Status)
                            {
                                batteryLevelCharacteristic = getCharacteristics.Characteristics[0];
                                await GetBatteryLevel();
                            }
                            else
                            {
                                Log($"Failed to obtain BTSIG_BatteryLevel characteristic {BTSIG_BatteryLevel}", LoggingLevel.Error);
                                DeviceDisconnect();
                            }
                        }
                        else
                        {
                            Log($"Failed to obtain BTSIG_BatteryService {BTSIG_BatteryService}", LoggingLevel.Error);
                            DeviceDisconnect();
                        }
                    }
                    else
                    {
                        Log($"Failed to acquire BluetoothLEDevice on {bluetoothId:x}", LoggingLevel.Error);
                        // Remove failed key from cache
                        localSettings.Values.Remove(BluetoothIDKey);
                        DeviceDisconnect();
                    }
                }
                else
                {
                    Log($"DeviceConnect found no Bluetooth ID, queue task to listen for advertisements.");
                    ListenForBluetoothAdvertisement();
                }
            }
        }

        private async void DeviceDisconnect(bool tryReconnect = true)
        {
            Log("Disconnecting device");
            batteryLevelCharacteristic?.Service.Dispose();
            batteryLevelCharacteristic = null;
            device.Dispose();
            device = null;
            if (tryReconnect)
            {
                Log("Retrying connect");
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, DeviceConnect);
            }
        }

        private void ListenForBluetoothAdvertisement()
        {
            if (watcher == null)
            {
                watcher = new BluetoothLEAdvertisementWatcher();
                watcher.Received += Bluetooth_Advertisement_Watcher_Received;
                watcher.Start();
                Log("Listening for Bluetooth advertisement broadcast by Sylvac indicator...", LoggingLevel.Information);
            }
            else
            {
                Log("BluetoothLEAdvertisementWatcher already exists, should not be trying to create a new watcher.", LoggingLevel.Warning);
            }
        }

        private async void Bluetooth_Advertisement_Watcher_Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            if (args.Advertisement.LocalName == IndicatorName)
            {
                Log($"Sylvac indicator advertisement received with address {args.BluetoothAddress:x}", LoggingLevel.Information);
                ApplicationData.Current.LocalSettings.Values[BluetoothIDKey] = args.BluetoothAddress;

                // We have what we need, halt watcher.
                watcher.Received -= Bluetooth_Advertisement_Watcher_Received;
                watcher.Stop();
                watcher = null;

                // Queue task to pull out the address and try connecting to it.
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, DeviceConnect);
            }
            else
            {
                Log($"Received advertisement from {args.BluetoothAddress:x} but doesn't sound like Sylvac indicator");
            }
        }

        private async Task<UInt16> GetBatteryLevel()
        {
            if (batteryLevelCharacteristic != null)
            {
                GattReadResult readResult = await batteryLevelCharacteristic.ReadValueAsync();
                if (GattCommunicationStatus.Success == readResult.Status)
                {
                    UInt16 batteryLevel = (UInt16)readResult.Value.GetByte(0);
                    Log($"Read battery level {batteryLevel}");
                    return batteryLevel;
                }
                else
                {
                    Log("Failed to read from battery level characteristic.");
                }
            }
            else
            {
                Log("GetBatteryLevel can't do anything if batteryLevelCharacteristic is NULL. Should have been setup upon device connect.", LoggingLevel.Error);
            }
            return 0;
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
    }
}
