using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
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
        private const string IndicatorName = "SY289";

        // Defined by author of this application
        private const string BluetoothIDKey = "BluetoothID";
        private const string MetricDisplayKey = "MetricDisplay";

        // Objects to interact with Bluetooth device, sorted roughly in order of usage
        private BluetoothLEAdvertisementWatcher watcher = null;
        private BluetoothLEDevice device = null;
        private GattCharacteristic batteryLevelCharacteristic = null;
        private GattCharacteristic measurementCharacteristic = null;
        private GattCharacteristic unitCharacteristic = null;

        // Objects for application housekeeping
        private DispatcherTimer activityUpdateTimer;
        private Logger logger = null;

        private int batteryLevel = 0;
        private TimeSpan batteryUpdate = new TimeSpan(0, 1 /* minute */, 0);
        private DateTime lastBatteryUpdate = DateTime.MaxValue;

        private Int32 measurementValue = 0;
        private Int32 measurementExponent = 1;

        private string preMeasurement = null;

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

            // Listen for time to cleanup
            Application.Current.Suspending += App_Suspending;
        }

        private async void DeviceConnect()
        {
            if (device != null)
            {
                Log("DeviceConnect should not be called when device is already connected", LoggingLevel.Error);
                throw new InvalidOperationException("DeviceConnect should not be called when device is already connected");
            }

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.ContainsKey(BluetoothIDKey))
            {
                preMeasurement = "Connecting...";
                ulong bluetoothId = (ulong)localSettings.Values[BluetoothIDKey];
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothId);
                if (device != null)
                {
                    Log($"Acquired BluetoothLEDevice {device.Name}", LoggingLevel.Information);
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
                Log($"DeviceConnect found no Bluetooth ID, queue task to listen for advertisements.", LoggingLevel.Information);
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
            Log($"GattCommunicationStatus.Success: {message}");
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
            CheckStatus(notify, $"Set notify for: {message}");
        }

        private async Task ClearNotification(GattCharacteristic characteristic, string message)
        {
            CheckCharacteristicFlag(characteristic, GattCharacteristicProperties.Notify, message);
            GattCommunicationStatus notify = await
                characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            CheckStatus(notify, $"Clear notify for: {message}");
        }

        private void CheckRead(GattCharacteristic characteristic, string message)
        {
            CheckCharacteristicFlag(characteristic, GattCharacteristicProperties.Read, message);
        }

        private void IOError(string message)
        {
            Log(message, LoggingLevel.Error);
            throw new IOException(message);
        }

        private async Task GetBLECharacteristics()
        {
            GattDeviceServicesResult getServices;
            GattCharacteristicsResult getCharacteristics;

            // Battery level
            getServices = await device.GetGattServicesForUuidAsync(BTSIG_BatteryService);
            CheckStatus(getServices.Status, "GetGattServicesForUuidAsync(BTSIG_BatteryService)");

            getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_BatteryLevel);
            CheckStatus(getCharacteristics.Status, "GetCharacteristicsForUuidAsync(BTSIG_BatteryLevel)");

            batteryLevelCharacteristic = getCharacteristics.Characteristics[0];
            lastBatteryUpdate = DateTime.MinValue;

            // Push notification of measurement change
            getServices = await device.GetGattServicesForUuidAsync(BTSIG_Unknown_Service);
            CheckStatus(getServices.Status, "GetGattServicesForUuidAsync(BTSIG_Unknown_Service)");

            getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_Unknown_Measurement);
            CheckStatus(getCharacteristics.Status, "GetCharacteristicsForUuidAsync(BTSIG_Unknown_Measurement)");

            measurementCharacteristic = getCharacteristics.Characteristics[0];

            getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_Unknown_Unit);
            CheckStatus(getCharacteristics.Status, "GetCharacteristicsForUuidAsync(BTSIG_Unknown_Unit)");

            unitCharacteristic = getCharacteristics.Characteristics[0];

            await CheckMeasurementPresentation();
            await StartNotifications();
        }

        private async Task CheckMeasurementPresentation()
        {
            GattDescriptorsResult getDescriptors;
            GattReadResult readResult;

            getDescriptors = await measurementCharacteristic.GetDescriptorsForUuidAsync(BTSIG_PresentationFormat);
            CheckStatus(getDescriptors.Status, "GetDescriptorsForUuidAsync(BTSIG_PresentationFormat)");

            readResult = await getDescriptors.Descriptors[0].ReadValueAsync();
            CheckStatus(readResult.Status, "BTSIG_PresentationFormat.ReadValueAsync()");

            if (7 != readResult.Value.Length)
            {
                IOError($"Presentation Format expected to have 7 bytes but had {readResult.Value.Length}");
            }

            if (0x10 != readResult.Value.GetByte(0))
            {
                IOError($"Data format expected to be 0x10 (32-bit signed int) but is 0x{readResult.Value.GetByte(0):x}");
            }

            measurementExponent = (sbyte)readResult.Value.GetByte(1);
            Log($"Measurement exponent {measurementExponent}");

            if (0x2701 != BitConverter.ToUInt16(readResult.Value.ToArray(2, 2), 0))
            {
                IOError($"Expected to be 0x2701 signifying meters, but read 0x{BitConverter.ToUInt16(readResult.Value.ToArray(2, 2), 0):x}");
            }
        }

        private async Task StartNotifications()
        {
            Log($"Start notifications", LoggingLevel.Information);
            await SetupNotification(measurementCharacteristic, "BTSIG_Unknown_Measurement");
            measurementCharacteristic.ValueChanged += Notification_Measurement;

            await SetupNotification(unitCharacteristic, "BTSIG_Unknown_Unit");
            unitCharacteristic.ValueChanged += Notification_Unit;
        }

        private async Task StopNotifications()
        {
            if (measurementCharacteristic != null)
            {
                Log($"Stop measurement notification", LoggingLevel.Information);
                measurementCharacteristic.ValueChanged -= Notification_Measurement;
                await ClearNotification(measurementCharacteristic, "BTSIG_Unknown_Measurement");
            }

            if (unitCharacteristic != null)
            {
                Log($"Stop unit notification", LoggingLevel.Information);
                unitCharacteristic.ValueChanged -= Notification_Unit;
                await ClearNotification(unitCharacteristic, "BTSIG_Unknown_Unit");
            }
        }

        private void Notification_Unit(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            UnitFlags unit = (UnitFlags)BitConverter.ToInt16(args.CharacteristicValue.ToArray(), 0);
            if (unit.HasFlag(UnitFlags.Metric))
            {
                Log($"Unit change notification METRIC", LoggingLevel.Information);
                ApplicationData.Current.LocalSettings.Values[MetricDisplayKey] = true;
            }
            else if (unit.HasFlag(UnitFlags.Inch))
            {
                Log($"Unit change notification INCH", LoggingLevel.Information);
                ApplicationData.Current.LocalSettings.Values[MetricDisplayKey] = false;
            }
            else
            {
                Log($"Notified of unit change has neither METRIC nor INCH flag: {unit}", LoggingLevel.Warning);
            }
        }

        private void Notification_Measurement(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            measurementValue = BitConverter.ToInt32(args.CharacteristicValue.ToArray(), 0);
            Log($"Received measurement update {measurementValue}");
            preMeasurement = null;
        }

        private async void DeviceDisconnect(bool tryReconnect = true)
        {
            Log("Disconnecting device (if present)", LoggingLevel.Information);
            try
            {
                batteryLevelCharacteristic?.Service.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // This happens when Bluetooth device disappears, for example
                // when it runs out of battery
                Log($"Battery level service already disposed.");
            }
            batteryLevelCharacteristic = null;
            device?.Dispose();
            device = null;
            if (tryReconnect)
            {
                preMeasurement = "Reconnecting...";
                Log("Retrying connect", LoggingLevel.Information);
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, DeviceConnect);
            }
            else
            {
                preMeasurement = "Disconnected";
            }
        }

        private void ListenForBluetoothAdvertisement()
        {
            if (watcher != null)
            {
                throw new InvalidOperationException("BluetoothLEAdvertisementWatcher already exists, should not be trying to create a new watcher.");
            }

            preMeasurement = "Searching...";
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

        private async Task GetBatteryLevel()
        {
            if (batteryLevelCharacteristic == null)
            {
                throw new InvalidOperationException(
                    "GetBatteryLevel can't do anything if batteryLevelCharacteristic is NULL. Should have been setup upon DeviceConnect.");
            }

            GattReadResult readResult = await batteryLevelCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            CheckStatus(readResult.Status, "batteryLevelCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached)");

            batteryLevel = (UInt16)readResult.Value.GetByte(0);
            Log($"GetBatteryLevel : {batteryLevel}%");
        }

        private async void ActivityUpdateTimer_Tick(object sender, object e)
        {
            bool isMetric = true;
            double displayValue;

            tbLogging.Text = logger.Recent;
            tbClock.Text = DateTime.UtcNow.ToString("yyyyMMddHHmmssff");

            if (preMeasurement == null)
            {
                if (ApplicationData.Current.LocalSettings.Values.ContainsKey(MetricDisplayKey))
                {
                    isMetric = (bool)ApplicationData.Current.LocalSettings.Values[MetricDisplayKey];
                }
                else
                {
                    // Typically metric measurements are reported in increments of 10
                    // Inch measurements are in increments of 12.7, so usually does not
                    //   divide evenly into 10 but sometimes it would.
                    // When we see something that doesn't neatly divide into 10, it's a
                    //   pretty good bet we're in inch mode. But if it divides into 10,
                    //   it is inconclusive.
                    if (0 != measurementValue % 10)
                    {
                        isMetric = false;
                        ApplicationData.Current.LocalSettings.Values[MetricDisplayKey] = false;
                    }
                    Log($"Inferring: units as metric? {isMetric}", LoggingLevel.Warning);
                }

                if (isMetric)
                {
                    displayValue = measurementValue * Math.Pow(10, measurementExponent + 3); // 10^3 millimeters in a meter
                    tbMeasurementValue.Text = $"{displayValue,6:f3}mm";
                }
                else
                {
                    displayValue = measurementValue * Math.Pow(10, measurementExponent);
                    displayValue *= 39.37008; // inches in a meter
                    tbMeasurementValue.Text = $"{displayValue,8:f5}\"";
                }

                if (DateTime.UtcNow - lastBatteryUpdate > batteryUpdate)
                {
                    lastBatteryUpdate = DateTime.UtcNow;
                    try
                    {
                        await GetBatteryLevel();
                        tbBatteryPercentage.Text = $"Estimate {batteryLevel}% Battery Remaining";

                        // Battery icon from Segoe MDL2 https://docs.microsoft.com/en-us/windows/uwp/design/style/segoe-ui-symbol-font
                        UInt16 glyphPoint;
                        if (batteryLevel > 95)
                        {
                            glyphPoint = 0xE83F; // Battery10
                        }
                        else
                        {
                            // Battery0 - Battery9
                            glyphPoint = 0xE850;
                            glyphPoint += (UInt16)(batteryLevel / 10);
                        }
                        fiBattery.Glyph = ((char)glyphPoint).ToString();
                    }
                    catch (ObjectDisposedException ode)
                    {
                        Log($"Bluetooth LE connection lost. {ode}", LoggingLevel.Information);
                        DeviceDisconnect();
                    }
                }
            }
            else
            {
                tbMeasurementValue.Text = preMeasurement;
            }
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

        private async void btResyncNotify_Click(object sender, RoutedEventArgs e)
        {
            await StopNotifications();
            await StartNotifications();
        }
        private async void App_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            await StopNotifications();
            Log("Cleaning up Bluetooth LE", LoggingLevel.Information);
            batteryLevelCharacteristic?.Service.Dispose();
            batteryLevelCharacteristic = null;
            measurementCharacteristic?.Service.Dispose();
            measurementCharacteristic = null;
            unitCharacteristic = null;
            device.Dispose();
            device = null;
            deferral.Complete();
        }
    }
}
