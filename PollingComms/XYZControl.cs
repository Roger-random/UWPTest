using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation.Diagnostics;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;

namespace PollingComms
{
    class XYZControl
    {
        const uint READ_BLOCK_SIZE = 4096;

        private CoreDispatcher dispatcher;
        private SerialDevice device;
        private DataReader reader;
        private DataWriter writer;
        private bool opened;

        public XYZControl()
        {
            dispatcher = null;
            device = null;
            reader = null;
            writer = null;
            opened = false;
        }

        public bool IsOpen
        {
            get
            {
                return opened;
            }
        }

        public async Task<bool> Open(DeviceInformation deviceInfo)
        {
            Close();

            Log($"XYZControl attempting to open {deviceInfo.Id}");
            try
            {
                device = await SerialDevice.FromIdAsync(deviceInfo.Id);
                if (device != null)
                {
                    device.BaudRate = 250000;
                    device.DataBits = 8;
                    device.StopBits = SerialStopBitCount.One;
                    device.Parity = SerialParity.None;
                    device.ReadTimeout = new TimeSpan(1000000);
                    device.WriteTimeout = new TimeSpan(1000000);

                    device.IsDataTerminalReadyEnabled = true; // Default is false, apparently required to be true to talk to RAMPS board.

                    reader = new DataReader(device.InputStream);
                    writer = new DataWriter(device.OutputStream);

                    reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                    writer.UnicodeEncoding = UnicodeEncoding.Utf8;
                    reader.InputStreamOptions = InputStreamOptions.ReadAhead;

                    CancellationTokenSource cancelSrc = new CancellationTokenSource(5000); // Cancel after 5000 milliseconds
                    Log($"Serial read {READ_BLOCK_SIZE} expecting hello text");
                    uint loadedSize = await reader.LoadAsync(READ_BLOCK_SIZE).AsTask<uint>(cancelSrc.Token);
                    Log($"reader.LoadAsync returned {loadedSize}");
                    string helloText = reader.ReadString(loadedSize);
                    Log(helloText);

                    int pulseIdx = helloText.IndexOf("Pulse D-224");
                    if (pulseIdx == -1)
                    {
                        Log($"Puse D-224 identifier string not found from device at.{deviceInfo.Id}", LoggingLevel.Information);
                        Close();
                    }
                    else
                    {
                        Log($"Pulse D-224 connected successfully at {deviceInfo.Id}", LoggingLevel.Information);
                        opened = true;
                    }
                }
                else
                {
                    Log($"XYZControl.Open failed since null was returned for {deviceInfo.Id}", LoggingLevel.Information);
                    Close();
                }
            }
            catch(TaskCanceledException)
            {
                Log($"XYZControl.Open timed out reading from {deviceInfo.Id}", LoggingLevel.Information);
            }
            catch(Exception e)
            {
                Log(e.ToString(), LoggingLevel.Error);
                Close();
            }

            return opened;
        }

        public void BeginReadLoop(CoreDispatcher uiDispatcher)
        {
            dispatcher = uiDispatcher;
            dispatcher.RunAsync(CoreDispatcherPriority.Low, ReadLoop);
        }

        public async void ReadLoop()
        {
            if (reader == null)
            {
                Log("No DataReader available for ReadLoop to work, exiting.", LoggingLevel.Error);
                return;
            }

            uint readSize = await reader.LoadAsync(READ_BLOCK_SIZE);
            if (readSize > 0)
            {
                Log($"ReadLoop retrieved {readSize} bytes.");
                try
                {
                    string readText = reader.ReadString(readSize);
                    Log(readText);
                }
                catch(InvalidOperationException ioe)
                {
                    Log(ioe.ToString(), LoggingLevel.Error);
                }
            }

            if (dispatcher != null)
            {
                dispatcher.RunAsync(CoreDispatcherPriority.Low, ReadLoop);
            }
        }

        private async void SendCommandAsync(string command)
        {
            if (writer == null)
            {
                Log($"No DataWriter available to send {command}", LoggingLevel.Error);
                return;
            }
            writer.WriteString(command);
            await writer.StoreAsync();
        }

        public void Home()
        {
            SendCommandAsync("G28\n");
        }

        public void MiddleIsh()
        {
            SendCommandAsync("G1 X125 Y125 Z125 F8000\n");
        }

        public void GetPos()
        {
            SendCommandAsync("M114\n");
        }

        public void Close()
        {
            if(writer != null)
            {
                writer.Dispose();
                writer = null;
            }
            if (reader != null)
            {
                reader.Dispose();
                reader = null;
            }
            if (device != null)
            {
                device.Dispose();
                device = null;
            }
            opened = false;
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
    }
}
