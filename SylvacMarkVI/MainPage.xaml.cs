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
using Windows.System.Display;
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
    /// Application that communicates with a Sylvac Mark VI digital indicator via Bluetooth LE
    /// </summary>

    /* This is an annotated result of enumerating every service, characteristic,
     * and descriptor on a particular unit of Sylvac Mark VI. Identifiers specific
     * to this unit has been obfuscated.
     * This application only utilizes a subset, the remainder are here only as reference.

    SY289 at Bluetooth address (obfuscated)
    Device with ID BluetoothLE#BluetoothLE00:28:f8:38:c9:34-(obfuscated) acquired.
      Generic Access Service 00001800-0000-1000-8000-00805f9b34fb
        Device Name Characteristic 00002a00-0000-1000-8000-00805f9b34fb property Read
           Retrieved value 0x5359323839 = "SY289"
        Appearance Characteristic 00002a01-0000-1000-8000-00805f9b34fb property Read
           Retrieved value 0x4005
        Peripheral preferred connection parameters Characteristic 00002a04-0000-1000-8000-00805f9b34fb property Read
           Retrieved value 0xffffffff0000ffff
      Generic Attribute Service 00001801-0000-1000-8000-00805f9b34fb
      (Vendor specific?) Service c1b25000-caaf-6d0e-4c33-7dae30052840
        Characteristic c1b25010-caaf-6d0e-4c33-7dae30052840 property Indicate
          Client characteristic configuration Descriptor 00002902-0000-1000-8000-00805f9b34fb value 0x0000
        Characteristic c1b25012-caaf-6d0e-4c33-7dae30052840 property WriteWithoutResponse
        Characteristic c1b25013-caaf-6d0e-4c33-7dae30052840 property Notify
          Client characteristic configuration Descriptor 00002902-0000-1000-8000-00805f9b34fb value 0x0000
        Characteristic c1b25014-caaf-6d0e-4c33-7dae30052840 property Read, WriteWithoutResponse
           Retrieved value 0x00
        Characteristic c1b25015-caaf-6d0e-4c33-7dae30052840 property Read, WriteWithoutResponse
           Retrieved value 0x00
        Characteristic c1b25016-caaf-6d0e-4c33-7dae30052840 property Read, WriteWithoutResponse
           Retrieved value 0x00
      Device Information Service 0000180a-0000-1000-8000-00805f9b34fb
        Model Number Characteristic 00002a24-0000-1000-8000-00805f9b34fb property Read
           Retrieved value 0x383035363530343131 = "805650411"
        Serial Number Characteristic 00002a25-0000-1000-8000-00805f9b34fb property Read
           Retrieved value (obfuscated)
        Firmware Revision Characteristic 00002a26-0000-1000-8000-00805f9b34fb property Read
           Retrieved value 0x72342e313072 = "r4.10r"
        Manufacturer Name Characteristic 00002a29-0000-1000-8000-00805f9b34fb property Read
           Retrieved value 0x53796c766163 = "Sylvac"
        Hardware Revision Characteristic 00002a27-0000-1000-8000-00805f9b34fb property Read
           Retrieved value 0x6e52463830303144 = "nRF8001D"
      Battery Service Service 0000180f-0000-1000-8000-00805f9b34fb
        Battery Level Characteristic 00002a19-0000-1000-8000-00805f9b34fb property Read
           Retrieved value 0x32 = 50
      (0x5000 + Bluetooth Base UUID) Service 00005000-0000-1000-8000-00805f9b34fb
        Characteristic 00005020-0000-1000-8000-00805f9b34fb property Read, Notify
          Characteristic Presentation Format Descriptor 00002904-0000-1000-8000-00805f9b34fb value 0x10f90127010000
          Client characteristic configuration Descriptor 00002902-0000-1000-8000-00805f9b34fb value 0x0000
           Retrieved value 0xffffff7f
        Characteristic 00005021-0000-1000-8000-00805f9b34fb property Notify
          Client characteristic configuration Descriptor 00002902-0000-1000-8000-00805f9b34fb value 0x0000

     */

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
        private DisplayRequest displayRequest;
        private Logger logger = null;

        // Track battery level (though not confident data is accurate)
        private int batteryLevel = 0;
        private TimeSpan batteryUpdateInterval = new TimeSpan(1 /* hour */, 0, 0);
        private DateTime lastBatteryUpdate = DateTime.MinValue;

        // Track we haven't lost communication with the indicator
        private TimeSpan heartbeatInterval = new TimeSpan(0, 1 /* minute */, 0);
        private DateTime lastCommunication = DateTime.MinValue;

        // Value and exponent of measurement received via push notification
        private Int32 measurementValue = 0;
        private Int32 measurementExponent = 1;

        // When non-null, a status string shown to the user to indicate
        // measurement value is yet to come.
        private string preMeasurementText = null;

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

            // While we are running, don't blank out the screen or activate screen saver.
            // https://blogs.windows.com/windowsdeveloper/2016/05/24/how-to-prevent-screen-locks-in-your-uwp-apps/
            displayRequest = new DisplayRequest();
            displayRequest.RequestActive();

            // Listen for application events
            Application.Current.EnteredBackground += App_EnteredBackground;
            Application.Current.LeavingBackground += App_LeavingBackground;
            Application.Current.Suspending += App_Suspending;
            Application.Current.Resuming += App_Resuming;
        }

        private void App_EnteredBackground(object sender, Windows.ApplicationModel.EnteredBackgroundEventArgs e)
        {
            Log("App.EnteredBackground");

            // No need to update UI while in background
            activityUpdateTimer.Stop();
        }

        private void App_LeavingBackground(object sender, Windows.ApplicationModel.LeavingBackgroundEventArgs e)
        {
            Log("App.LeavingBackground");

            // Resuming updating UI when brought out of background
            activityUpdateTimer.Start();
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
                preMeasurementText = "Connecting...";
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
                throw new InvalidOperationException(message);
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

            // Push notification of measurement change
            getServices = await device.GetGattServicesForUuidAsync(BTSIG_Unknown_Service);
            CheckStatus(getServices.Status, "GetGattServicesForUuidAsync(BTSIG_Unknown_Service)");

            getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_Unknown_Measurement);
            CheckStatus(getCharacteristics.Status, "GetCharacteristicsForUuidAsync(BTSIG_Unknown_Measurement)");

            measurementCharacteristic = getCharacteristics.Characteristics[0];

            getCharacteristics = await getServices.Services[0].GetCharacteristicsForUuidAsync(BTSIG_Unknown_Unit);
            CheckStatus(getCharacteristics.Status, "GetCharacteristicsForUuidAsync(BTSIG_Unknown_Unit)");

            unitCharacteristic = getCharacteristics.Characteristics[0];

            preMeasurementText = "Waiting...";
            await GetBatteryLevel();
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
            lastCommunication = DateTime.UtcNow;
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
            lastCommunication = DateTime.UtcNow;
            measurementValue = BitConverter.ToInt32(args.CharacteristicValue.ToArray(), 0);
            Log($"Received measurement update {measurementValue}");
            preMeasurementText = null;
        }

        private async void DeviceDisconnect(bool tryReconnect = true)
        {
            Log("Disconnecting device (if present)", LoggingLevel.Information);
            try
            {
                batteryLevelCharacteristic?.Service.Dispose();
                measurementCharacteristic?.Service.Dispose();
                // unitCharacteristic is from the same Service, doesn't need a separate Dispose() call.
            }
            catch (ObjectDisposedException)
            {
                // This happens when Bluetooth device disappears, for example
                // when it runs out of battery
                Log($"DeviceDisconnect can't dispose services already disposed.");
            }
            batteryLevelCharacteristic = null;
            measurementCharacteristic = null;
            unitCharacteristic = null;
            device?.Dispose();
            device = null;
            if (tryReconnect)
            {
                preMeasurementText = "Reconnecting...";
                Log("Retrying connect", LoggingLevel.Information);
                await Dispatcher.RunAsync(CoreDispatcherPriority.Low, DeviceConnect);
            }
            else
            {
                preMeasurementText = "Disconnected";
            }
        }

        private void ListenForBluetoothAdvertisement()
        {
            if (watcher != null)
            {
                throw new InvalidOperationException("BluetoothLEAdvertisementWatcher already exists, should not be trying to create a new watcher.");
            }

            preMeasurementText = "Searching...";
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

            lastCommunication = DateTime.UtcNow;
            lastBatteryUpdate = DateTime.UtcNow;
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

            if (preMeasurementText == null)
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
            }
            else
            {
                tbMeasurementValue.Text = preMeasurementText;
            }

            // Battery level query also acts as a device heartbeat. Polled at
            // regular intervals to make sure the device is still there.
            if (batteryLevelCharacteristic != null && 
                (DateTime.UtcNow - lastCommunication > heartbeatInterval ||
                 DateTime.UtcNow - lastBatteryUpdate > batteryUpdateInterval))
            {
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
            try
            {
                await StopNotifications();
            }
            catch(Exception error)
            {
                // Encountered problems trying to stop notifications, but this is not
                // necessary a fatal issue...
                Log($"Resync attempt to stop notifications: {error}", LoggingLevel.Error);
            }
            try
            {
                await StartNotifications();
            }
            catch(Exception error)
            {
                // Encountered problems trying to restart notifications, this is a
                // bigger problem. Try disconnect & reconnect with device.
                Log($"Resync attempt to restart notifications: {error}", LoggingLevel.Error);
                DeviceDisconnect(/* tryReconnect = */ true);
            }
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
        private async void App_Resuming(object sender, object e)
        {
            // Queue task to connect to device via BLE
            await Dispatcher.RunAsync(CoreDispatcherPriority.Low, DeviceConnect);
        }
    }
}
