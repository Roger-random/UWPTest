using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace SerialQueryTest
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private DispatcherTimer _activityUpdateTimer;
        private Logger _logger = null;

        public MainPage()
        {
            this.InitializeComponent();

            ActivityUpdateTimer_Start();

            if (Application.Current as App != null)
            {
                _logger = ((App)Application.Current).logger;
            }
        }

        private void ActivityUpdateTimer_Start()
        {
            _activityUpdateTimer = new DispatcherTimer();
            _activityUpdateTimer.Tick += ActivityUpdateTimer_Tick;
            _activityUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, 200 /* milliseconds */);
            _activityUpdateTimer.Start();
        }

        private void ActivityUpdateTimer_Tick(object sender, object e)
        {
            tbLogging.Text = _logger.Recent;
            tbClock.Text = DateTime.UtcNow.ToString("yyyyMMddHHmmssff");
        }
    }
}
