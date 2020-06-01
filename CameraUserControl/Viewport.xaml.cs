using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Diagnostics;
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
        public Viewport()
        {
            this.InitializeComponent();
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
