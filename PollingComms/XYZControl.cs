using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation.Diagnostics;
using Windows.Storage.Streams;
using Windows.UI.Xaml;

namespace PollingComms
{
    class XYZControl
    {
        private SerialDevice device;
        private DataReader reader;
        private DataWriter writer;
        private bool opened;

        public XYZControl()
        {
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
                    device.ReadTimeout = new TimeSpan(0, 0, 1);
                    device.WriteTimeout = new TimeSpan(0, 0, 2);

                    device.IsDataTerminalReadyEnabled = true; // Default is false, apparently required to be true to talk to RAMPS board.

                    reader = new DataReader(device.InputStream);
                    writer = new DataWriter(device.OutputStream);

                    reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                    writer.UnicodeEncoding = UnicodeEncoding.Utf8;
                    reader.InputStreamOptions = InputStreamOptions.ReadAhead;

                    CancellationTokenSource cancelSrc = new CancellationTokenSource(5000); // Cancel after 5000 milliseconds
                    Log("4K serial read expecting hello text");
                    uint loadedSize = await reader.LoadAsync(4096).AsTask<uint>(cancelSrc.Token);
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
