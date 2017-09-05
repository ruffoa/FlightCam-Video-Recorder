using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Xceed.Wpf.Toolkit;

namespace CameraApplication
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : Window
    {
        bool manualChanged = false;
        public Settings()
        {
            InitializeComponent();
            populateFields();
        }

        private void populateFields()
        {
            gpsComName.Content = Properties.Settings.Default.gpsCommPortName;
            gpsComBaud.Content = Properties.Settings.Default.gpsComBaudRate;
            gpsAutosetOn.IsChecked = Properties.Settings.Default.autoCheckGPSCommPort;
            gpsManualSet_on.IsChecked = Properties.Settings.Default.useManualGPSComPort;

            if (Properties.Settings.Default.manualGpsComPortBaud > 0)
                gpsComBaudSet.SelectedValue = Properties.Settings.Default.manualGpsComPortBaud;
            if (Properties.Settings.Default.manualGpsComPortName.Length > 0)
                comPortList.SelectedValue = Properties.Settings.Default.manualGpsComPortName;

            altitudeUseMeters.IsChecked = Properties.Settings.Default.altitudeInMeeters;
            altitudeUseFeet.IsChecked = !Properties.Settings.Default.altitudeInMeeters;
            speedUseMperS.IsChecked = Properties.Settings.Default.speedInMeterSec;
            speedUseKnots.IsChecked = !Properties.Settings.Default.speedInMeterSec;
            useManualCamIp.IsChecked = Properties.Settings.Default.useManualCamUrl;
            useDefaultCamIp.IsChecked = !Properties.Settings.Default.useManualCamUrl;
            int temp = Properties.Settings.Default.CamOneUrl.LastIndexOf(".") + 1;
            camLanIp.Content = Properties.Settings.Default.CamOneUrl.Substring(temp);

            if ((bool)Properties.Settings.Default.useManualCamUrl)
            {
                foreach (var v in SupportedIPAdresses)
                {
                    manualCamIp.Items.Add(v);
                    if (!manualCamIp.SelectedItems.Contains(v))
                    {
                        if (v.ToString().Contains(Properties.Settings.Default.CamOneUrlManual.Substring(0, Properties.Settings.Default.CamOneUrlManual.LastIndexOf("."))) || v.ToString().Contains(Properties.Settings.Default.CamTwoUrlManual.Substring(0, Properties.Settings.Default.CamTwoUrlManual.LastIndexOf("."))) || v.ToString().Contains(Properties.Settings.Default.CamThreeUrlManual.Substring(0, Properties.Settings.Default.CamThreeUrlManual.LastIndexOf("."))))
                            manualCamIp.SelectedItems.Add(v);
                    }

                }
                manualLanIP.Text = Properties.Settings.Default.CamOneUrlManual.Substring(Properties.Settings.Default.CamOneUrlManual.LastIndexOf(".") + 1);
            }
            else
                foreach (var v in SupportedIPAdresses)
                    manualCamIp.Items.Add(v);
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void CloseCommandHandler(object sender, ExecutedRoutedEventArgs e)
        {
            this.Close();
        }

        private void save_btn_Click(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.gpsCommPortName = gpsComName.Content.ToString();
            Properties.Settings.Default.gpsComBaudRate = Int32.Parse(gpsComBaud.Content.ToString());
            Properties.Settings.Default.autoCheckGPSCommPort = gpsAutosetOn.IsChecked.Value;
            Properties.Settings.Default.useManualGPSComPort = gpsManualSet_on.IsChecked.Value;

            if (gpsManualSet_on.IsChecked.Value)
            {
                if (gpsComBaudSet.SelectedValue != null)
                    Properties.Settings.Default.manualGpsComPortBaud = (int)gpsComBaudSet.SelectedValue;
                if (comPortList.SelectedValue != null)
                    Properties.Settings.Default.manualGpsComPortName = comPortList.SelectedValue.ToString();

                if (!manualChanged)
                    System.Windows.MessageBox.Show(
                        "You must restart the application in order to use the new Com Port \n"
                        ,
                        "Restart Neccesary",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                manualChanged = false;

            }

            //// Units
            Properties.Settings.Default.altitudeInMeeters = (bool)altitudeUseMeters.IsChecked;
            Properties.Settings.Default.speedInMeterSec = (bool)speedUseMperS.IsChecked;

            //// manual Camera IP Settings
            Properties.Settings.Default.useManualCamUrl = (bool)useManualCamIp.IsChecked;

            if ((bool)useManualCamIp.IsChecked)
            {
                var selIPs = manualCamIp.SelectedItems;

                String temp = selIPs[0].ToString();
                temp = temp.Substring(0, temp.IndexOf("x"));
                Properties.Settings.Default.CamOneUrlManual = temp + manualLanIP.Text;

                if (selIPs.Count > 1)
                {
                    temp = selIPs[1].ToString();
                    temp = temp.Substring(0, temp.IndexOf("x"));
                    Properties.Settings.Default.CamTwoUrlManual = temp + manualLanIP.Text;
                }
                if (selIPs.Count > 2)
                {
                    temp = selIPs[2].ToString();
                    temp = temp.Substring(0, temp.IndexOf("x"));  // TODO: implement dynamic loading to allow for more than three cameras (and change other calls to allow for this as well)
                    Properties.Settings.Default.CamThreeUrlManual = temp + manualLanIP.Text;
                }
            }

            Properties.Settings.Default.Save();
            this.Close();
        }

        private readonly List<int> SupportedBaudRates = new List<int>
{
    300,
    600,
    1200,
    2400,
    4800,
    9600,
    19200,
    38400,
    57600,
    115200,
    230400,
    460800,
    921600
};

        private readonly List<String> SupportedIPAdresses = new List<String>
{
     "192.168.0.x",
     "192.168.1.x",
     "192.168.2.x",
     "192.168.3.x",
     "192.168.4.x",
     "192.168.5.x",
     "192.168.6.x",
     "192.168.7.x",
     "192.168.8.x",
     "192.168.9.x",
};

        private void comPortList_Initialized(object sender, EventArgs e)
        {
            var ports = System.IO.Ports.SerialPort.GetPortNames().OrderBy(s => s);
            if (ports.Count() <= 0)
            {
                System.Windows.MessageBox.Show(
                        "Error: No Serial Ports avaliable.  Make sure the gps reciever is plugged in and installed.  Contact your system administrator for help. \n"
                        , "Error 1: No Serial Ports Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                comPortList.IsEnabled = false;
                return;
            }
            foreach (var p in ports)
                comPortList.Items.Add(p);

        }

        private void gpsComBaudSet_Initialized(object sender, EventArgs e)
        {
            foreach (var v in SupportedBaudRates)
                gpsComBaudSet.Items.Add(v);
        }

        private void comPortList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            manualChanged = true;
        }

        private void gpsComBaudSet_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            manualChanged = true;
        }

        private void gpsManualSet_on_Click(object sender, RoutedEventArgs e)
        {
            manualChanged = true;
        }

        private void manualLanIP_Initialized(object sender, EventArgs e)
        {

        }

        private void camIp_Initialized(object sender, EventArgs e)
        {
            foreach (var v in SupportedIPAdresses)
            {
                camIp.Items.Add(v);
                if (!camIp.SelectedItems.Contains(v))
                {
                    if (v.ToString().Contains(Properties.Settings.Default.CamOneUrl.Substring(0, Properties.Settings.Default.CamOneUrl.Length - 3)) || v.ToString().Contains(Properties.Settings.Default.CamThreeUrl.Substring(0, Properties.Settings.Default.CamThreeUrl.Length - 3)) || v.ToString().Contains(Properties.Settings.Default.CamTwoUrl.Substring(0, Properties.Settings.Default.CamTwoUrl.Length - 3)))
                        camIp.SelectedItems.Add(v);
                }

            }
        }
    }
}
