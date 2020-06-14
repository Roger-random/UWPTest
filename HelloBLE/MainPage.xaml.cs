using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.WiFiDirect;
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
        private bool dumping = false;
        private BluetoothLEDevice device = null;
        private List<ulong> btAddresses = new List<ulong>();

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

        private void LogBuffer(string header, IBuffer data)
        {
            DataReader dr = DataReader.FromBuffer(data);
            Byte[] dataArray = new Byte[data.Capacity];
            bool inASCII = true;
            dr.ReadBytes(dataArray);
            string dataDump = header;
            foreach (Byte d in dataArray)
            {
                dataDump += $"{d:x2}";
                if (d < 32 || d > 126)
                {
                    inASCII = false;
                }
            }
            if (inASCII)
            {
                try
                {
                    UTF8Encoding utf8 = new UTF8Encoding(false, true);
                    string dataAsString = utf8.GetString(dataArray);
                    dataDump += $" \"{dataAsString}\"";
                }
                catch (Exception)
                {
                    // Encountered problem interpreting as UTF8 string, skip string dump.
                }
            }
            Log(dataDump);
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

            if (tryConnecting || connected || btAddresses.Contains(args.BluetoothAddress))
            {
                return;
            }

            btAddresses.Add(args.BluetoothAddress);
            Log($"New advertisement from address {args.BluetoothAddress:x} with name {advertisement.LocalName}");
            tryConnecting = true;
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            if (device != null)
            {
                connected = true;
                tryConnecting = false;
                Log($"  BluetoothLEDevice Name={device.Name}");
                Log("-- Enumerating all BLE services, characteristics, and descriptors");

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
                                if (characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read))
                                {
                                    GattReadResult readResult = await characteristic.ReadValueAsync();
                                    if (GattCommunicationStatus.Success == readResult.Status)
                                    {
                                        LogBuffer($"      Read value 0x", readResult.Value);
                                    }
                                    else
                                    {
                                        Log($"      Failed to read from readable characteristic");
                                    }
                                }

                                GattDescriptorsResult getDescriptors = await characteristic.GetDescriptorsAsync();
                                if (GattCommunicationStatus.Success == getDescriptors.Status)
                                {
                                    foreach (GattDescriptor descriptor in getDescriptors.Descriptors)
                                    {
                                        GattReadResult readResult = await descriptor.ReadValueAsync();
                                        if (GattCommunicationStatus.Success == readResult.Status)
                                        {
                                            LogBuffer($"      Descriptor {descriptor.Uuid} value 0x", readResult.Value);
                                        }
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
                    foreach (GattDeviceService service in getGattServices.Services)
                    {
                        service.Session.Dispose();
                        service.Dispose();
                    }
                }
                else
                {
                    Log($"Getting GATT services failed with status {getGattServices.Status}");
                }

                // Dump complete, release device.
                device.Dispose();
                device = null;
                connected = false;
            }
            else
            {
                tryConnecting = false;
                Log($"  Failed to obtain BluetoothLEDevice from that advertisement");
            }
        }
    }
}
