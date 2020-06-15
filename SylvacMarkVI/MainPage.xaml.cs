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
        // Defined by Bluetooth Special Interest Group (BTSIG)
        private Guid BTSIG_BatteryService = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
        private Guid BTSIG_BatteryLevel = new Guid("00002a19-0000-1000-8000-00805f9b34fb");
        private Guid BTSIG_PresentationFormat = new Guid("00002904-0000-1000-8000-00805f9b34fb");

        // The following GUIDs are in the BTSIG space, but I couldn't find official names.
        private Guid BTSIG_Unknown_Service = new Guid("00005000-0000-1000-8000-00805f9b34fb");
        private Guid BTSIG_Unknown_Measurement = new Guid("00005020-0000-1000-8000-00805f9b34fb");
        private Guid BTSIG_Unknown_Unit = new Guid("00005021-0000-1000-8000-00805f9b34fb");

        // Best guess interpreting BTSIG_Unknown_Unit
        [Flags]
        private enum UnitFlags : UInt16
        {
            Metric = 4096, // 0001 0000 0000 0000
            Inch = 8192,   // 0010 0000 0000 0000
        }

        // Defined by Sylvac
        private Guid Sylvac_Service = new Guid("c1b25000-caaf-6d0e-4c33-7dae30052840");
        private Guid Sylvac_Service_Input = new Guid("c1b25012-caaf-6d0e-4c33-7dae30052840");
        private Guid Sylvac_Service_Output = new Guid("c1b25013-caaf-6d0e-4c33-7dae30052840");
        private const string IndicatorName = "SY289";

        // Defined by author of this application
        private const string BluetoothIDKey = "BluetoothID";
        private const string MetricDisplayKey = "MetricDisplay";

        // Objects to interact with Bluetooth device, sorted roughly in order of usage
        private BluetoothLEAdvertisementWatcher watcher = null;
        private BluetoothLEDevice device = null;
        private GattCharacteristic batteryLevelCharacteristic = null;
        private GattCharacteristic sylvacServiceInput = null;

        // Objects for application housekeeping
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
                return;
            }

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey(BluetoothIDKey))
            {
                ulong bluetoothId = (ulong)localSettings.Values[BluetoothIDKey];
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothId);
                if (device != null)
                {
                    Log($"Successfully acquired BluetoothLEDevice {device.Name}");
                    try
                    {
                        await GetBLECharacteristics();
                    }
                    catch(IOException e)
                    {
                        Log($"Failed to obtain BLE characteristics. {e.Message}", LoggingLevel.Error);
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

        private void CheckStatus(GattCommunicationStatus status, string message)
        {
            if(GattCommunicationStatus.Success != status)
            {
                Log($"CheckStatus throwing IOException {status} {message}", LoggingLevel.Error);
                throw new IOException($"{status} " + message);
            }
        }

        private void CheckCharacteristicFlag(GattCharacteristic characteristic, GattCharacteristicProperties flag, string message)
        {
            if (!characteristic.CharacteristicProperties.HasFlag(flag))
            {
                Log($"CheckCharacteristicFlag did not see {flag} for {message}", LoggingLevel.Error);
                throw new IOException(message);
            }
        }

        private async Task SetupNotification(GattCharacteristic characteristic, string message)
        {
            CheckCharacteristicFlag(characteristic, GattCharacteristicProperties.Notify, message);
            GattCommunicationStatus notify = await
                characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
            CheckStatus(notify, $"Notify for: {message}");
        }
        private void CheckRead(GattCharacteristic characteristic, string message)
        {
            CheckCharacteristicFlag(characteristic, GattCharacteristicProperties.Read, message);
        }

        private async Task GetBLECharacteristics()
        {
            GattDeviceServicesResult getServices;
            GattCharacteristicsResult getCharacteristics;
            GattCharacteristic characteristic;

            // Battery level
            getServices = await device.GetGattServicesForUuidAsync(BTSIG_BatteryService);
            CheckStatus(getServices.Status, "GetGattServicesForUuidAsync(BTSIG_BatteryService)");

            getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_BatteryLevel);
            CheckStatus(getCharacteristics.Status, "GetCharacteristicsForUuidAsync(BTSIG_BatteryLevel)");

            batteryLevelCharacteristic = getCharacteristics.Characteristics[0];
            await GetBatteryLevel();

            // Push notification of measurement change
            getServices = await device.GetGattServicesForUuidAsync(BTSIG_Unknown_Service);
            CheckStatus(getServices.Status, "GetGattServicesForUuidAsync(BTSIG_Unknown_Service)");

            getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_Unknown_Measurement);
            CheckStatus(getCharacteristics.Status, "GetCharacteristicsForUuidAsync(BTSIG_Unknown_Measurement)");

            characteristic = getCharacteristics.Characteristics[0];
            await SetupNotification(characteristic, "BTSIG_Unknown_Measurement");
            characteristic.ValueChanged += Notification_Measurement;

            getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_Unknown_Unit);
            CheckStatus(getCharacteristics.Status, "GetCharacteristicsForUuidAsync(BTSIG_Unknown_Unit)");

            characteristic = getCharacteristics.Characteristics[0];
            await SetupNotification(characteristic, "BTSIG_Unknown_Unit");
            characteristic.ValueChanged += Notification_Unit;
        }

        private void Notification_Unit(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            UnitFlags unit = (UnitFlags)BitConverter.ToInt16(args.CharacteristicValue.ToArray(), 0);
            if (unit.HasFlag(UnitFlags.Metric))
            {
                Log($"Unit change notification has METRIC flag");
                //ApplicationData.Current.LocalSettings.Values[MetricDisplayKey] = true;
            }
            else if (unit.HasFlag(UnitFlags.Inch))
            {
                Log($"Unit change notification has INCH flag");
                //ApplicationData.Current.LocalSettings.Values[MetricDisplayKey] = false;
            }
            else
            {
                Log($"Notified of unit change: {unit}");
            }
        }

        private void Notification_Measurement(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            Int32 measurement = BitConverter.ToInt32(args.CharacteristicValue.ToArray(), 0);
            Log($"Received measurement update {measurement}");
        }

        private async void DeviceDisconnect(bool tryReconnect = true)
        {
            Log("Disconnecting device (if present)");
            batteryLevelCharacteristic?.Service.Dispose();
            batteryLevelCharacteristic = null;
            device?.Dispose();
            device = null;
            if (tryReconnect)
            {
                Log("Retrying connect");
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, DeviceConnect);
            }
        }

        private void ListenForBluetoothAdvertisement()
        {
            if (watcher != null)
            {
                throw new InvalidOperationException("BluetoothLEAdvertisementWatcher already exists, should not be trying to create a new watcher.");
            }

            watcher = new BluetoothLEAdvertisementWatcher();
            watcher.Received += Bluetooth_Advertisement_Watcher_Received;
            watcher.Start();
            Log("Listening for Bluetooth advertisement broadcast by Sylvac indicator...", LoggingLevel.Information);
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
                Log($"Received advertisement from {args.BluetoothAddress:x} but doesn't sound like Sylvac indicator. Continue listening...");
            }
        }

        private async Task<UInt16> GetBatteryLevel()
        {
            if (batteryLevelCharacteristic == null)
            {
                throw new InvalidOperationException(
                    "GetBatteryLevel can't do anything if batteryLevelCharacteristic is NULL. Should have been setup upon DeviceConnect.");
            }

            GattReadResult readResult = await batteryLevelCharacteristic.ReadValueAsync();
            CheckStatus(readResult.Status, "batteryLevelCharacteristic.ReadValueAsync()");

            UInt16 batteryLevel = (UInt16)readResult.Value.GetByte(0);
            Log($"GetBatteryLevel : {batteryLevel}%");
            return batteryLevel;
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
