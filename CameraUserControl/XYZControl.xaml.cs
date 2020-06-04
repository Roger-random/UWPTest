using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace CameraUserControl
{
    public sealed partial class XYZControl : UserControl
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

        private PeripheralStatus statusControl;

        private int serialDevice = 0;

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
        private double targetX;
        private double targetY;
        private double targetZ;
        private bool haveNewTarget = false;

        private double moveStep = 10;

        private DispatcherTimer updateTargetCoordinateTimer;

        public XYZControl()
        {
            this.InitializeComponent();

            Application.Current.Suspending += Current_Suspending;

            dispatcher = null;
            device = null;
            reader = null;
            lastReadTime = DateTime.MinValue;
            writer = null;
            opened = false;
            crQueue = new Queue<TaskCompletionSource<List<String>>>();
            responsesSoFar = null;

            updateTargetCoordinateTimer = new DispatcherTimer();
            updateTargetCoordinateTimer.Tick += UpdateTargetCoordinateTimer_Tick;
            updateTargetCoordinateTimer.Interval = new TimeSpan(0, 0, 0, 0, 250);
            updateTargetCoordinateTimer.Start();
        }

        private void UpdateTargetCoordinateTimer_Tick(object sender, object e)
        {
            ExecuteMoveToTarget();
        }

        public PeripheralStatus StatusControl
        {
            get
            {
                return statusControl;
            }
            set
            {
                if (value != null)
                {
                    statusControl = value;
                    statusControl.Label = "XYZ Control";
                    statusControl.StatusText = "Connecting...";

                    Connect();
                }
                else
                {
                    statusControl.Label = "N/A";
                    statusControl.StatusColor = Colors.Gray;
                    statusControl.StatusText = "Disconnected";
                    statusControl = null;

                    Close();
                }
            }
        }

        public (double, double, double) Position
        {
            get
            {
                return (positionX, positionY, positionZ);
            }
            set
            {
                (double tX, double tY, double tZ) = value;
                _ = ValidateAndMove(tX, tY, tZ);
            }
        }

        private bool ValidateAndMove(double tX, double tY, double tZ)
        {
            if (tX >= 0 && tX <= 200)
            {
                if (tY >= 0 && tY <= 200)
                {
                    if (tZ >= 0 && tZ <= 200)
                    {
                        if (tX != positionX || tY != positionY || tZ != positionZ)
                        {
                            targetX = tX;
                            targetY = tY;
                            targetZ = tZ;
                            haveNewTarget = true;
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        public void ExecuteMoveToTarget()
        {
            if (haveNewTarget)
            {
                SendCommandWaitResponseAsync($"G1 X{targetX} Y{targetY} Z{targetZ} F12000");
                SendCommandWaitResponseAsync("M114"); // Update coordinates

                haveNewTarget = false;
            }
            return;
        }

        private async void Connect()
        {
            bool openSuccess = false;

            if (device != null)
            {
                Close();
            }

            // TODO: Enumerate serial devices instead of hard-coding one.
            // The @ prefix means verbatim. https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/verbatim
            openSuccess = await Open(@"\\?\USB#VID_1A86&PID_7523#6&27620D36&0&1#{86e0d1e0-8089-11d0-9ce4-08003e301f73}");

            if (openSuccess)
            {
                statusControl.StatusText = "Connected";
                statusControl.StatusColor = Colors.Green;
                BeginReadAsync(Dispatcher);
            }
        }

        public async Task<bool> Open(string deviceId)
        {
            Close();

            Log($"XYZControl attempting to open {deviceId}");
            try
            {
                device = await SerialDevice.FromIdAsync(deviceId);
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

                    int pulseIdx = helloText.IndexOf("Marlin");
                    if (pulseIdx == -1)
                    {
                        Log($"Marlin identifier string not found from device at.{deviceId}", LoggingLevel.Information);
                        Close();
                    }
                    else
                    {
                        Log($"Marlin (or compatible) 3D printer connected successfully at {deviceId}", LoggingLevel.Information);
                        opened = true;
                    }
                }
                else
                {
                    Log($"XYZControl.Open failed since null was returned for {deviceId}", LoggingLevel.Information);
                    Close();
                }
            }
            catch (TaskCanceledException)
            {
                Log($"XYZControl.Open timed out reading from {deviceId}", LoggingLevel.Information);
            }
            catch (Exception e)
            {
                Log(e.ToString(), LoggingLevel.Error);
                Close();
            }

            return opened;
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
                serialDevice++;
            }
            foreach (TaskCompletionSource<List<String>> tcs in crQueue)
            {
                tcs.SetCanceled();
            }
            crQueue.Clear();
        }

        public async void BeginReadAsync(CoreDispatcher uiDispatcher)
        {
            dispatcher = uiDispatcher;
            await dispatcher.RunAsync(CoreDispatcherPriority.Low, ReadLoop);
        }

        private void AddToResponseList(string line)
        {
            if (responsesSoFar == null)
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
                    Log($"Last successful read at {lastReadTime}", LoggingLevel.Information);
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

                    tbXcoord.Text = String.Format("{0,8:N2}", positionX);
                    tbYcoord.Text = String.Format("{0,8:N2}", positionY);
                    tbZcoord.Text = String.Format("{0,8:N2}", positionZ);

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
            catch (TaskCanceledException)
            {
                Log($"No response due to cancellation of {command}");
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

        private void Current_Suspending(object sender, Windows.ApplicationModel.SuspendingEventArgs e)
        {
            Close();
        }

        private void HomingCycle()
        {
            SendCommandWaitResponseAsync("G28");
            targetX = targetY = targetZ = 0;
        }

        private void btnHome_Click(object sender, RoutedEventArgs e)
        {
            HomingCycle();
        }

        private void ValidateAndMoveDelta(double deltaX, double deltaY, double deltaZ)
        {
            ValidateAndMove(targetX + deltaX, targetY + deltaY, targetZ + deltaZ);
        }

        private void btnXPos_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndMoveDelta(moveStep, 0, 0);
        }

        private void btnXNeg_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndMoveDelta(-moveStep, 0, 0);
        }

        private void btnYPos_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndMoveDelta( 0, moveStep, 0);
        }

        private void btnYNeg_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndMoveDelta(0, -moveStep, 0);
        }

        private void btnZPos_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndMoveDelta(0, 0, moveStep);
        }

        private void btnZNeg_Click(object sender, RoutedEventArgs e)
        {
            ValidateAndMoveDelta(0, 0, -moveStep);
        }

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            Log("XYZControl.OnKeyDown");
            switch (e.Key)
            {
                case VirtualKey.Home:
                    HomingCycle();
                    e.Handled = true;
                    break;
                case VirtualKey.Up:
                    ValidateAndMoveDelta(0, moveStep, 0);
                    e.Handled = true;
                    break;
                case VirtualKey.Down:
                    ValidateAndMoveDelta(0, -moveStep, 0);
                    e.Handled = true;
                    break;
                case VirtualKey.Right:
                    ValidateAndMoveDelta(moveStep, 0, 0);
                    e.Handled = true;
                    break;
                case VirtualKey.Left:
                    ValidateAndMoveDelta(-moveStep, 0, 0);
                    e.Handled = true;
                    break;
                case VirtualKey.PageUp:
                    ValidateAndMoveDelta(0, 0, moveStep);
                    e.Handled = true;
                    break;
                case VirtualKey.PageDown:
                    ValidateAndMoveDelta(0, 0, -moveStep);
                    e.Handled = true;
                    break;
            }

            base.OnKeyDown(e);
        }
        protected override void OnKeyUp(KeyRoutedEventArgs e)
        {
            Log("XYZControl.OnKeyUp");
            base.OnKeyUp(e);
        }

    }
}
