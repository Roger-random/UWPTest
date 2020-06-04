using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Diagnostics;
using Windows.Graphics.Display;
using Windows.Media.Capture;
using Windows.System.Display;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace CameraUserControl
{
    public sealed partial class Viewport : UserControl
    {
        private PeripheralStatus cameraStatus = null;
        private MediaCapture mediaCapture = null;
        private bool isPreviewing = false;
        private DisplayRequest displayRequest = new DisplayRequest();
        private DeviceInformationCollection cameraCollection;
        private int nextCamera = 0;

        public Viewport()
        {
            this.InitializeComponent();
            this.Loaded += Viewport_Loaded;
            this.SizeChanged += Viewport_SizeChanged;

            Application.Current.Suspending += OnSuspending;
        }

        public async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();

            await CleanupAsync();

            deferral.Complete();
        }

        public async Task CleanupAsync()
        {
            Log("Viewport.CleanupAsync");

            if (mediaCapture != null)
            {
                if (isPreviewing)
                {
                    await mediaCapture.StopPreviewAsync();
                    Log("Viewport: MediaCapture.StopPreviewAsync");
                }

                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    cameraPreview.Source = null;

                    mediaCapture.Dispose();
                    mediaCapture = null;
                    Log("Viewport: MediaCapture Disposed");

                    if (displayRequest != null)
                    {
                        displayRequest.RequestRelease();
                        Log("Viewport: DisplayRequest.RequestRelease");
                    }
                });
            }
        }

        private async void Viewport_Loaded(object sender, RoutedEventArgs e)
        {
            // Find the camera status indicator
            Frame rootFrame = Window.Current.Content as Windows.UI.Xaml.Controls.Frame;
            MainPage mainPage = rootFrame.Content as MainPage;

            cameraStatus = mainPage.CameraStatus;

            cameraStatus.Label = "Camera";

            // Get list of cameras on this device
            cameraCollection = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            Log($"Found {cameraCollection.Count} VideoCapture devices");

            if (cameraCollection.Count > 0)
            {
                cameraStatus.StatusButton.Click += StatusButton_Click;
                cameraStatus.StatusText = "Not Connected";
                nextCamera = 0;
            }
            else
            {
                cameraStatus.StatusText = "No cameras found";
            }

            await ToggleCameraState();
        }

        private async void StatusButton_Click(object sender, RoutedEventArgs e)
        {
            await ToggleCameraState();
        }

        private async Task ToggleCameraState()
        {
            if (mediaCapture==null)
            {
                try
                {
                    MediaCaptureInitializationSettings settings = new MediaCaptureInitializationSettings();
                    settings.VideoDeviceId = cameraCollection[nextCamera].Id;
                    mediaCapture = new MediaCapture();
                    await mediaCapture.InitializeAsync(settings); // Docs say must be from UI thread
                    Log("MediaCapture Initialized");

                    displayRequest.RequestActive();
                    DisplayInformation.AutoRotationPreferences = DisplayOrientations.Landscape;
                }
                catch (UnauthorizedAccessException)
                {
                    cameraStatus.StatusText = "Permission denied";
                    Log("Camera access denied", LoggingLevel.Error);
                    mediaCapture.Dispose();
                    mediaCapture = null;
                    return;
                }
            }

            if (isPreviewing)
            {
                await CleanupAsync();
                cameraStatus.StatusColor = Colors.Gray;
                cameraStatus.StatusText = "Not connected";
                isPreviewing = false;

                nextCamera++;
                if (nextCamera >= cameraCollection.Count)
                {
                    nextCamera = 0;
                }
            }
            else
            {
                try
                {
                    cameraPreview.Source = mediaCapture;
                    await mediaCapture.StartPreviewAsync();
                    isPreviewing = true;
                    Log("Viewport preview started");
                    cameraStatus.StatusColor = Colors.Green;
                    cameraStatus.StatusText = cameraCollection[nextCamera].Name;
                }
                catch (System.IO.FileLoadException)
                {
                    cameraStatus.StatusText = "Camera in use";
                    Log("Unable to obtain control of camera", LoggingLevel.Error);
                }
            }
        }

        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double circleDiameter = 10;
            double circleWidth = 1;

            circleIn.Width = circleIn.Height = circleDiameter - (circleWidth*2);
            circleMid.Width = circleMid.Height = circleDiameter;
            circleOut.Width = circleOut.Height = circleDiameter + (circleWidth*2);
        }

        private void Log(string t, LoggingLevel level = LoggingLevel.Verbose)
        {
            App app = Application.Current as App;

            if (app == null)
            {
                // This occurs in design mode.
                return;
            }
            Logger logger = app.logger;
            if (logger != null)
            {
                logger.Log(t, level);
            }
            else
            {
                Debug.WriteLine("WARNING: Logger not available, log message lost.");
            }
        }

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            Log("Viewport.OnKeyDown");
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyRoutedEventArgs e)
        {
            Log("Viewport.OnKeyUp");
            base.OnKeyUp(e);
        }

        protected override void OnPointerPressed(PointerRoutedEventArgs e)
        {
            Log("Viewport.OnPointerPressed");
            base.OnPointerPressed(e);
        }

        protected override void OnPointerReleased(PointerRoutedEventArgs e)
        {
            Log("Viewport.OnPointerReleased");
            base.OnPointerReleased(e);
        }
    }
}
