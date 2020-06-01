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
            this.SizeChanged += Viewport_SizeChanged;
        }

        private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PointCollection crosshairPoints;
            double centerHeight = e.NewSize.Height / 2.0;
            double centerWidth = e.NewSize.Width / 2.0;
            double circleDiameter = 0;

            double crosshairWidth = 1;
            double crosshairGap = 0.025;
            double circleFraction = 0.2;

            Log($"Viewport.SizeChanged to {e.NewSize}");
            if (e.NewSize.Height > e.NewSize.Width)
            {
                circleDiameter = e.NewSize.Width * circleFraction;
            }
            else
            {
                circleDiameter = e.NewSize.Height * circleFraction;
            }

            circleIn.Width = circleIn.Height = circleDiameter - (crosshairWidth*2);
            circleMid.Width = circleMid.Height = circleDiameter;
            circleOut.Width = circleOut.Height = circleDiameter + (crosshairWidth*2);

            crosshairPoints = new PointCollection();
            crosshairPoints.Add(new Point(0, centerHeight - crosshairWidth));
            crosshairPoints.Add(new Point(centerWidth * (1 - crosshairGap), centerHeight - crosshairWidth));
            crosshairPoints.Add(new Point(centerWidth * (1 - crosshairGap), centerHeight + crosshairWidth));
            crosshairPoints.Add(new Point(0, centerHeight + crosshairWidth));
            crosshairL.Points = crosshairPoints;

            crosshairPoints = new PointCollection();
            crosshairPoints.Add(new Point(e.NewSize.Width, centerHeight - crosshairWidth));
            crosshairPoints.Add(new Point(centerWidth * (1 + crosshairGap), centerHeight - crosshairWidth));
            crosshairPoints.Add(new Point(centerWidth * (1 + crosshairGap), centerHeight + crosshairWidth));
            crosshairPoints.Add(new Point(e.NewSize.Width, centerHeight + crosshairWidth));
            crosshairR.Points = crosshairPoints;

            crosshairPoints = new PointCollection();
            crosshairPoints.Add(new Point(centerWidth - crosshairWidth, 0));
            crosshairPoints.Add(new Point(centerWidth - crosshairWidth, centerHeight * (1 - crosshairGap)));
            crosshairPoints.Add(new Point(centerWidth + crosshairWidth, centerHeight * (1 - crosshairGap)));
            crosshairPoints.Add(new Point(centerWidth + crosshairWidth, 0));
            crosshairU.Points = crosshairPoints;

            crosshairPoints = new PointCollection();
            crosshairPoints.Add(new Point(centerWidth - crosshairWidth, e.NewSize.Height));
            crosshairPoints.Add(new Point(centerWidth - crosshairWidth, centerHeight * (1 + crosshairGap)));
            crosshairPoints.Add(new Point(centerWidth + crosshairWidth, centerHeight * (1 + crosshairGap)));
            crosshairPoints.Add(new Point(centerWidth + crosshairWidth, e.NewSize.Height));
            crosshairD.Points = crosshairPoints;
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
