using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        private enum ResponseType
        {
            Unknown,            // We don't know how to treat this response.
            Ignore,             // This line is not part of a command response, and can be ignored.
            BlockPartial,       // This line is part of response for a command, but not the end.
            BlockEnd            // This line marks the successful end of a block of response.
        }

        private CoreDispatcher dispatcher;
        private SerialDevice device;
        private bool opened;
        private DataWriter writer;
        private DataReader reader;
        private DateTime lastReadTime;
        private Queue<TaskCompletionSource<List<String>>> crQueue;
        private List<String> responsesSoFar;
        private double positionX;
        private double positionY;
        private double positionZ;


        public XYZControl()
        {
            dispatcher = null;
            device = null;
            reader = null;
            writer = null;
            opened = false;
            crQueue = new Queue<TaskCompletionSource<List<String>>>();
            responsesSoFar = null;
        }

        public bool IsOpen
        {
            get
            {
                return opened;
            }
        }

        public double X
        {
            get
            {
                return positionX;
            }
        }

        public double Y
        {
            get
            {
                return positionY;
            }
        }

        public double Z
        {
            get
            {
                return positionZ;
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

        private void AddToResponseList(string line)
        {
            if (responsesSoFar==null)
            {
                responsesSoFar = new List<String>();
            }
            responsesSoFar.Add(line);
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
                lastReadTime = DateTime.UtcNow;
                if (readSize > 0)
                {
                    Log($"ReadLoop retrieved {readSize} bytes.");
                    try
                    {
                        StringReader readText = new StringReader(reader.ReadString(readSize));
                        string line = null;
                        ResponseType respType = ResponseType.Unknown;

                        while (null != (line = readText.ReadLine()))
                        {
                            respType = ProcessResponseLine(line);
                            if (respType == ResponseType.BlockPartial)
                            {
                                AddToResponseList(line);
                            }
                            else if (respType == ResponseType.BlockEnd)
                            {
                                AddToResponseList(line);
                                if (crQueue.Count > 0)
                                {
                                    TaskCompletionSource<List<String>> tcs = crQueue.Dequeue();
                                    tcs.SetResult(responsesSoFar);
                                    responsesSoFar = null;
                                }
                                else
                                {
                                    Log($"No command queued to correspond to response.", LoggingLevel.Error);
                                    foreach (String lostLine in responsesSoFar)
                                    {
                                        Log($"Lost response: {lostLine}", LoggingLevel.Information);
                                    }
                                    responsesSoFar.Clear();
                                }
                            }
                            else if (respType == ResponseType.Ignore)
                            {
                                Log($"Ignoring {line}");
                            }
                            else
                            {
                                Log($"Shutting down due to unexpected response {line}", LoggingLevel.Error);
                                Close();
                            }
                        }
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

        private bool ParseCoordinates(string line)
        {
            string[] components = line.Split(new char[] { ' ', ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (components.Length >= 6 &&
                components[0].ToUpperInvariant() == "X" &&
                components[2].ToUpperInvariant() == "Y" &&
                components[4].ToUpperInvariant() == "Z")
            {
                try
                {
                    positionX = double.Parse(components[1]);
                    positionY = double.Parse(components[3]);
                    positionZ = double.Parse(components[5]);

                    return true;
                }
                catch (Exception e)
                {
                    Log($"Error encountered trying to parse as coordinate: {line}", LoggingLevel.Error);
                    Log(e.ToString());
                }
            }
            return false;
        }

        private ResponseType ProcessResponseLine(string line)
        {
            ResponseType resType = ResponseType.Unknown;

            if (line.StartsWith("echo:busy: processing") ||
                line.StartsWith("echo:SD "))
            {
                resType = ResponseType.Ignore;
            }
            else if (ParseCoordinates(line))
            {
                resType = ResponseType.BlockPartial;
            }
            else if (line.StartsWith("ok"))
            {
                resType = ResponseType.BlockEnd;
            }

            return resType;
        }

        private async void WriterStore(string command)
        {
            writer.WriteString($"{command}\n");
            await writer.StoreAsync();
        }

        private Task<List<String>> SendCommandAsync(string command)
        {
            TaskCompletionSource<List<String>> taskCompletionSource = new TaskCompletionSource<List<String>>();

            if (writer == null)
            {
                Log($"No DataWriter available to send {command}", LoggingLevel.Error);
                taskCompletionSource.SetCanceled();
            }
            else
            {
                Log($"Sending {command}");
                try
                {
                    WriterStore(command);
                    crQueue.Enqueue(taskCompletionSource);
                }
                catch (Exception e)
                {
                    Log("Unable to send command due to communication error, closing port.", LoggingLevel.Error);
                    Log(e.ToString(), LoggingLevel.Information);
                    Close();
                    taskCompletionSource.SetException(e);
                }
            }

            return taskCompletionSource.Task;
        }

        private async void SendCommandWaitResponseAsync(string command)
        {
            List<String> responses;

            try
            {
                responses = await SendCommandAsync(command);
                foreach (String line in responses)
                {
                    Log($"{command} response {line}");
                }
            }
            catch(TaskCanceledException)
            {
                Log($"No response due to cancellation of {command}");
            }
        }

        public void Home()
        {
            SendCommandWaitResponseAsync("G28");
        }

        public void MiddleIsh()
        {
            SendCommandWaitResponseAsync("G1 X125 Y125 Z125 F8000");
        }

        public void GetPos()
        {
            SendCommandWaitResponseAsync("M114");
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
            foreach(TaskCompletionSource<List<String>> tcs in crQueue)
            {
                tcs.SetCanceled();
            }
            crQueue.Clear();
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
