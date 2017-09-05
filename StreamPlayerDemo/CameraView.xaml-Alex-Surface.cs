﻿using Microsoft.Win32;
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

namespace CameraApplication
{
    /// <summary>
    /// Interaction logic for CameraView.xaml
    /// </summary>
    public partial class CameraView : Window
    {
        public CameraView()
        {
            InitializeComponent();
            _urlTextBox.IsEnabled =  (bool)useCustomURLChk.IsChecked;
        }

        private void HandlePlayButtonClick(object sender, RoutedEventArgs e)
        {
            loadingGif.Visibility = Visibility.Visible;

            Uri uri;
            Uri uri2;
            Uri uri3;
            //uri = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing

            if (!Properties.Settings.Default.useManualCamUrl)
            {
                if ((bool)useCustomURLChk.IsChecked)
                    uri = new Uri(_urlTextBox.Text);
                else
                    uri = new Uri("rtsp://" + Properties.Settings.Default.CamOneUrl + "/axis-media/media.amp");
                uri2 = new Uri("rtsp://" + Properties.Settings.Default.CamTwoUrl + "/axis-media/media.amp");
                uri3 = new Uri("rtsp://" + Properties.Settings.Default.CamThreeUrl + "/axis-media/media.amp");
            }
            else
            {
                uri = new Uri("rtsp://" + Properties.Settings.Default.CamOneUrlManual + "/axis-media/media.amp");
                uri2 = new Uri("rtsp://" + Properties.Settings.Default.CamTwoUrlManual + "/axis-media/media.amp");
                uri3 = new Uri("rtsp://" + Properties.Settings.Default.CamThreeUrlManual + "/axis-media/media.amp");
            }

            if ((bool)useOldFormat.IsChecked)
            {
                uri = new Uri("rtsp://192.168.1.200/axis-media/media.amp");
                uri2 = new Uri("rtsp://192.168.2.200/axis-media/media.amp");
                uri3 = new Uri("rtsp://192.168.3.200/axis-media/media.amp");
            }

            _streamPlayerControl.StartPlay(uri, TimeSpan.FromSeconds(15));
            _statusLabel.Text = "Connecting...";
            videoStatus.Text = "Connecting Main...";

            //var uri2 = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing
            //var uri3 = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing

            _streamPlayerControl2.StartPlay(uri2, TimeSpan.FromSeconds(15));
            videoStatus.Text = "Connecting Secondary...";
            _streamPlayerControl3.StartPlay(uri3, TimeSpan.FromSeconds(15));
            videoStatus.Text = "Connecting Stream 3...";

        }

        private void HandleStopButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _streamPlayerControl.Stop();
            }
            catch { }
            try
            {
                _streamPlayerControl2.Stop();
            }
            catch
            {

            }
            try
            {
                _streamPlayerControl3.Stop();
            }
            catch
            {

            }
        }

        private void HandleImageButtonClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "Bitmap Image|*.bmp" };
            if (dialog.ShowDialog() == true)
            {
                _streamPlayerControl.GetCurrentFrame().Save(dialog.FileName);
            }
        }

        private void UpdateButtons()
        {
            _playButton.IsEnabled = !_streamPlayerControl.IsPlaying;
            _stopButton.IsEnabled = _streamPlayerControl.IsPlaying;
            _imageButton.IsEnabled = _streamPlayerControl.IsPlaying;
            if (_streamPlayerControl.IsPlaying)
                loadingGif.Visibility = Visibility.Hidden;
            else
                loadingGif.Visibility = Visibility.Visible;

        }

        private void HandlePlayerEvent(object sender, RoutedEventArgs e)
        {
            UpdateButtons();

            if (e.RoutedEvent.Name == "StreamStarted")
            {
                _statusLabel.Text = "Playing";
            }
            else if (e.RoutedEvent.Name == "StreamFailed")
            {
                _statusLabel.Text = "Failed";

                MessageBox.Show(
                    ((WebEye.StreamFailedEventArgs)e).Error + Environment.NewLine + "Check your stream URL and your camera to make sure your camera is set up correctly.  Camera URLs can be changed in Settings. ",
                    "Camera Stream Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                loadingGif.Visibility = Visibility.Hidden;

            }
            else if (e.RoutedEvent.Name == "StreamStopped")
            {
                _statusLabel.Text = "Stopped";
            }
        }


        private void _streamPlayerControl2_Loaded(object sender, RoutedEventArgs e)
        {

        }
        private void _streamPlayerControl_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void _closePrev_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void useCustomURLChk_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)useCustomURLChk.IsChecked)
                _urlTextBox.IsEnabled = true;
            else
                _urlTextBox.IsEnabled = false;
        }
    }
}
