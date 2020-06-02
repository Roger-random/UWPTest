using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
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
    public sealed partial class PeripheralStatus : UserControl
    {
        public PeripheralStatus()
        {
            this.InitializeComponent();
        }

        public Windows.UI.Color StatusColor
        {
            get
            {
                return ((SolidColorBrush)btnConnect.Background).Color;
            }
            set
            {
                btnConnect.Background = new SolidColorBrush(value);
            }
        }

        public string Label
        {
            get
            {
                return tbLabel.Text;
            }
            set
            {
                tbLabel.Text = value;
            }
        }
    }
}
