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
        const uint TIMESPAN_MILLISECOND = 10000; // 100 nanoseconds * 10000 = 1ms
        const uint READ_TIMEOUT = 200 * TIMESPAN_MILLISECOND;
        const uint WRITE_TIMEOUT = 200 * TIMESPAN_MILLISECOND;

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
                    device.ReadTimeout = new TimeSpan(READ_TIMEOUT);
                    device.WriteTimeout = new TimeSpan(WRITE_TIMEOUT);

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

        public async void BeginReadAsync(CoreDispatcher uiDispatcher)
        {
            dispatcher = uiDispatcher;
            await dispatcher.RunAsync(CoreDispatcherPriority.Low, ReadLoop);
        }

        public async void ReadLoop()
        {
            if (reader == null)
            {
                Log("No DataReader available for ReadLoop to work, exiting.", LoggingLevel.Error);
                return;
            }

            try
            {
                uint readSize = await reader.LoadAsync(READ_BLOCK_SIZE);
                if (readSize > 0)
                {
                    Log($"ReadLoop retrieved {readSize} bytes.");
                    try
                    {
                        string readText = reader.ReadString(readSize);
                        Log(readText);
                    }
                    catch (InvalidOperationException ioe)
                    {
                        Log(ioe.ToString(), LoggingLevel.Error);
                    }
                }

                if (dispatcher != null)
                {
                    await dispatcher.RunAsync(CoreDispatcherPriority.Low, ReadLoop);
                }
            }
            catch (Exception e)
            {
                if (opened)
                {
                    Log("Read loop terminated due to unexpected loss of communication with device.", LoggingLevel.Error);
                    Log(e.ToString(), LoggingLevel.Information);
                    Close();
                }
                else
                {
                    Log("Read loop terminated due to closing port.", LoggingLevel.Information);
                }
            }
        }

        private async void SendCommandAsync(string command)
        {
            if (writer == null)
            {
                Log($"No DataWriter available to send {command}", LoggingLevel.Error);
                return;
            }
            Log($"Sending {command}");
            try
            {
                writer.WriteString($"{command}\n");
                await writer.StoreAsync();
            }
            catch (Exception e)
            {
                Log("Unable to send command due to communication error, closing port.", LoggingLevel.Error);
                Log(e.ToString(), LoggingLevel.Information);
                Close();
            }
        }

        public void Home()
        {
            SendCommandAsync("G28");
        }

        public void MiddleIsh()
        {
            SendCommandAsync("G1 X125 Y125 Z125 F8000");
        }

        public void GetPos()
        {
            SendCommandAsync("M114");
        }

        public void Close()
        {
            opened = false;
            if (writer != null)
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
