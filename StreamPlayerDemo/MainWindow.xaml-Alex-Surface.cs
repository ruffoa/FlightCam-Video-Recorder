using System;
using System.Windows;
using Microsoft.Win32;
using System.IO.Ports;
using System.Threading;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using System.ComponentModel;
using System.Management; // need to add System.Management for usb drive access

namespace CameraApplication
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        static SerialPort serialPort;
        static bool _continue = true;
        bool serialAvaliable = false;
        bool _showDebugInfo = false;
        private NmeaParser.NmeaDevice gpsSerialDevice;
        private Queue<string> messages = new Queue<string>(101);
        SerialPort autoSerialPort = null;
        int[] prcID = new int[3];
        static MainWindow main;
        static Process process;
        static Process cam2;
        static Process mergeVid;
        static CameraApplication.autoscanProgress prog;
        static String filename;

        static String camLog = "";

        private readonly BackgroundWorker worker = new BackgroundWorker()
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        public MainWindow()
        {
            main = this;
            InitializeComponent();
            _statusLabel.Text = "Connecting to serial port...";
            worker.DoWork += worker_DoWork;
            worker.RunWorkerCompleted += worker_RunWorkerCompleted;

            if (Properties.Settings.Default.autoCheckGPSCommPort && !Properties.Settings.Default.useManualGPSComPort)
                //autoSerialPort = FindPort();
                worker.RunWorkerAsync();
            else
                serialFunc();

            if (prog != null)
            {
                prog.Close();
                prog.Hide();
            }

            //serialFunc();
        }



        private void HandlePreviewButtonClick(object sender, RoutedEventArgs e)
        {
            CameraView cam = new CameraView();
            cam.Show();
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            // run all background tasks here
            //autoSerialPort = FindPort();
            IProgress<string> progress = null;

            var ports = System.IO.Ports.SerialPort.GetPortNames().OrderBy(s => s);
            if (ports.Count() <= 0)
            {
                MessageBox.Show(
                        "Error: No Serial Ports avaliable.  Make sure the gps reciever is plugged in and installed.  Contact your system administrator for help. \n"
                        , "Error 1: No Serial Ports Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                _continue = false;
                autoSerialPort = null;
                return;
            }
            Application.Current.Dispatcher.Invoke((Action)delegate
            {
                prog = new autoscanProgress();
                prog.Show();
            });


            int i = 0;
            int portNum = 0;
            int total = ports.Count() * 3;
            prog.scanPercentLabel.Dispatcher.BeginInvoke((Action)(() => prog.scanPercentLabel.Content = "Beginning Scan..."));
            prog.autoscanProgressBar.Dispatcher.BeginInvoke((Action)(() => prog.autoscanProgressBar.Value = 2));

            foreach (var portName in ports)
            {
                portNum++;
                using (var port = new System.IO.Ports.SerialPort(portName))
                {

                    var defaultRate = port.BaudRate;
                    List<int> baudRatesToTest = new List<int>(new[] { 9600, 4800, 19200 }); //Ordered by likelihood // removed: 115200, 57600, 38400, 2400
                                                                                            //Move default rate to first spot
                    if (baudRatesToTest.Contains(defaultRate)) baudRatesToTest.Remove(defaultRate);
                    baudRatesToTest.Insert(0, defaultRate);

                    foreach (var baud in baudRatesToTest)
                    {
                        i++;
                        double test = (double)(i) / total;
                        int percent = (int)(test * 100);
                        Trace.WriteLine("Percent complete: " + percent + "%");

                        Application.Current.Dispatcher.Invoke((Action)delegate
                        {
                            prog.autoscanProgressBar.Value = percent;
                            prog.scanPercentLabel.Content = "Scanning Port " + portNum + " at Baud " + baud;
                        });

                        if (progress != null)
                        {
                            progress.Report(string.Format("Trying {0} @ {1}baud", portName, port.BaudRate));
                            worker.ReportProgress(percent);
                        }
                        port.BaudRate = baud;
                        port.ReadTimeout = 2500; //this might not be long enough
                        bool success = false;
                        try
                        {
                            port.Open();
                            if (!port.IsOpen)
                                continue; //couldn't open port
                            try
                            {
                                port.ReadTo("$GP");
                                success = true;
                                Trace.WriteLine("GPS Found at: " + port.PortName + " baud " + port.BaudRate);
                            }
                            catch (TimeoutException)
                            {
                                continue;
                            }
                        }
                        catch
                        {
                            //Error reading
                        }
                        finally
                        {
                            port.Close();
                            //prog.Close();
                        }
                        if (success)
                        {
                            Application.Current.Dispatcher.Invoke((Action)delegate
                            {
                                prog.Close();
                            });
                            autoSerialPort = new System.IO.Ports.SerialPort(portName, baud);
                            return;
                        }
                    }
                }
            }
            //serialPort = new System.IO.Ports.SerialPort("COM6", 9600);
            //return serialPort;

            MessageBox.Show(
                       "Error: Could not AutoDetect a GPS device.  Make sure the gps reciever is plugged in and installed.  If you believe your device is properly installed, it may not be on a scanned baud rate (9600, 4800, 19200).  " +
                       "If this is the case, please go to settings and manually enter the information for your GPS reciever. \n" +
                       "Contact your system administrator for help. \n"
                       , "Error 1: No Serial Ports Found",
                   MessageBoxButton.OK,
                   MessageBoxImage.Error);
            autoSerialPort = null;
            Application.Current.Dispatcher.Invoke((Action)delegate
            {
                prog.Close();
            });
            return;

        }

        private void worker_RunWorkerCompleted(object sender,
                                               RunWorkerCompletedEventArgs e)
        {
            if (serialPort != null)
                _statusLabel.Text = "Connected to serial port " + serialPort.PortName;
            else
                _statusLabel.Text = "Serial port connection failed";

            Trace.WriteLine("AutoSet worker finished");
            serialFunc();

            //update ui once worker completes its work
        }


        private void _streamPlayerControl2_Loaded(object sender, RoutedEventArgs e)
        {

        }
        private void _streamPlayerControl_Loaded(object sender, RoutedEventArgs e)
        {

        }

        public void serialFunc()
        {
            messages.Clear();
            gprmcView.Message = null;
            gpggaView.Message = null;
            if (autoSerialPort == null && _continue == false)
                return;

            if (autoSerialPort != null)
            {
                serialPort = autoSerialPort;
            }
            else if (Properties.Settings.Default.useManualGPSComPort && Properties.Settings.Default.manualGpsComPortName.Length > 0 && Properties.Settings.Default.manualGpsComPortBaud > 0)
            {
                serialPort = new System.IO.Ports.SerialPort(Properties.Settings.Default.manualGpsComPortName, Properties.Settings.Default.manualGpsComPortBaud);
            }
            else if (Properties.Settings.Default.gpsCommPortName.Length > 0 && Properties.Settings.Default.gpsComBaudRate > 0)
                serialPort = new System.IO.Ports.SerialPort(Properties.Settings.Default.gpsCommPortName, Properties.Settings.Default.gpsComBaudRate); //use settings from last Autoset if manual is disabled
            else
            {
                _statusLabel.Text = "No Serial Ports Found";
                return;
            }
            Properties.Settings.Default.gpsCommPortName = serialPort.PortName;
            Properties.Settings.Default.gpsComBaudRate = serialPort.BaudRate;
            Properties.Settings.Default.autoCheckGPSCommPort = false;       // disable after port is found to prevent autoscanning at every launch
            Properties.Settings.Default.Save();

            gpsSerialDevice = new NmeaParser.SerialPortDevice(serialPort);
            gpsSerialDevice.MessageReceived += gpsdevice_MessageReceived;
            gpsSerialDevice.OpenAsync();
            _statusLabel.Text = serialPort.PortName + " Connected";

        }

        private void gpsdevice_MessageReceived(object sender, NmeaParser.NmeaMessageReceivedEventArgs args)
        {
            Dispatcher.BeginInvoke((Action)delegate ()
            {
                messages.Enqueue(args.Message.MessageType + ": " + args.Message.ToString());
                if (messages.Count > 100) messages.Dequeue(); //Keep message queue at 100
                if (_showDebugInfo)
                {
                    //gpsInfo.Text = string.Join("\n", messages.ToArray());
                    //gpsInfo.Select(gpsInfo.Text.Length - 1, 0); //scroll to bottom
                }
                if (args.Message is NmeaParser.Nmea.Gps.Gprmc)
                {
                    gprmcView.Message = args.Message as NmeaParser.Nmea.Gps.Gprmc;
                    double speed = gprmcView.Message.Speed;
                    speedArc.EndAngle = ((speed / 200) * 240) - 120;
                    if (Properties.Settings.Default.speedInMeterSec)
                    {
                        speedVal.Text = speed.ToString();
                        speedUnitText.Text = "m/s";
                    }
                    else
                    {
                        speedVal.Text = (speed * 1.94384).ToString();
                        speedUnitText.Text = "Knots";
                    }
                }
                else if (args.Message is NmeaParser.Nmea.Gps.Gpgga)
                {
                    gpggaView.Message = args.Message as NmeaParser.Nmea.Gps.Gpgga;
                    if (Properties.Settings.Default.altitudeInMeeters)
                    {
                        altitudeVal.Text = gpggaView.Message.Altitude.ToString();
                        altitudeUnit.Text = "Meters";  // gpggaView.Message.AltitudeUnits.ToString()
                    }
                    else
                    {
                        altitudeVal.Text = (gpggaView.Message.Altitude * 3.28084).ToString();
                        altitudeUnit.Text = "Feet";
                    }
                }
                //else if (args.Message is NmeaParser.Nmea.UnknownMessage)
                //{
                //    if (_showDebugInfo)
                //        gpsInfo.Text += "\n Error: Unknown Signal Type!";
                //}
                else
                {
                    // Do nothing
                }
            });
        }

        private static System.IO.Ports.SerialPort FindPort(IProgress<string> progress = null)
        {
            var ports = System.IO.Ports.SerialPort.GetPortNames().OrderBy(s => s);
            if (ports.Count() <= 0)
            {
                MessageBox.Show(
                        "Error: No Serial Ports avaliable.  Make sure the gps reciever is plugged in and installed.  Contact your system administrator for help. \n"
                        , "Error 1: No Serial Ports Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                _continue = false;
                return null;
            }
            prog = new autoscanProgress();
            prog.Show();

            int i = 0;
            int total = ports.Count() * 4;
            prog.scanPercentLabel.Dispatcher.BeginInvoke((Action)(() => prog.scanPercentLabel.Content = "Beginning Scan..."));
            prog.autoscanProgressBar.Dispatcher.BeginInvoke((Action)(() => prog.autoscanProgressBar.Value = 2));

            foreach (var portName in ports)
            {
                i++;
                int j = 0;
                using (var port = new System.IO.Ports.SerialPort(portName))
                {
                    j++;
                    var defaultRate = port.BaudRate;
                    List<int> baudRatesToTest = new List<int>(new[] { 9600, 4800, 19200 }); //Ordered by likelihood // removed: 115200, 57600, 38400, 2400
                                                                                            //Move default rate to first spot
                    if (baudRatesToTest.Contains(defaultRate)) baudRatesToTest.Remove(defaultRate);
                    baudRatesToTest.Insert(0, defaultRate);
                    foreach (var baud in baudRatesToTest)
                    {
                        //prog.autoscanProgressBar.Dispatcher.BeginInvoke((Action)(() => prog.autoscanProgressBar.Value = (double)(((i + j) / total) * 100)));
                        prog.SetProgress((int)(double)(((i + j) / total) * 100));
                        prog.scanPercentLabel.Dispatcher.BeginInvoke((Action)(() => prog.scanPercentLabel.Content = "Scanning Port " + i + " at Baud " + baud));

                        if (progress != null)
                            progress.Report(string.Format("Trying {0} @ {1}baud", portName, port.BaudRate));
                        port.BaudRate = baud;
                        port.ReadTimeout = 2000; //this might not be long enough
                        bool success = false;
                        try
                        {
                            port.Open();
                            if (!port.IsOpen)
                                continue; //couldn't open port
                            try
                            {
                                port.ReadTo("$GP");
                                success = true;
                            }
                            catch (TimeoutException)
                            {
                                continue;
                            }
                        }
                        catch
                        {
                            //Error reading
                        }
                        finally
                        {
                            port.Close();
                            //prog.Close();
                        }
                        if (success)
                        {
                            prog.Close();
                            return new System.IO.Ports.SerialPort(portName, baud);
                        }
                    }
                }
            }
            //serialPort = new System.IO.Ports.SerialPort("COM6", 9600);
            //return serialPort;

            MessageBox.Show(
                       "Error: Could not AutoDetect a GPS device.  Make sure the gps reciever is plugged in and installed.  If you believe your device is properly installed, it may not be on a scanned baud rate (9600, 4800, 19200).  " +
                       "If this is the case, please go to settings and manually enter the information for your GPS reciever. \n" +
                       "Contact your system administrator for help. \n"
                       , "Error 1: No Serial Ports Found",
                   MessageBoxButton.OK,
                   MessageBoxImage.Error);
            return null;
        }


        private void settingsBtn_Click(object sender, RoutedEventArgs e)
        {
            CameraApplication.Settings win2 = new CameraApplication.Settings();
            win2.Show();
        }

        private void cameraRecord_Click(object sender, RoutedEventArgs e)
        {
            filename = DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "-";
            filename = filename.Replace('/', '-');
            filename = filename.Replace(':', '-');
            filename = filename.Replace(' ', '-');
            var uri = new Uri(_urlTextBox.Text);
            recordInfo.Text += "Starting Record Process";
            int[] pid = startRecordProcess(uri, true);
            prcID = pid;
            recordInfo.Text += "\n started process with id(s) " + pid[0] + ", " + pid[1] + ", " + pid[2];
        }

        static int[] startRecordProcess(Uri uri, bool multiTrue)
        {
            //////////////////////// Merged Video ///////////////////////////////
            int[] pID = new int[3];
            string path = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).FullName;
            path += @"\" + filename + "output.mkv";


            process = new Process();
            //process.StartInfo.RedirectStandardOutput = true;
            //process.StartInfo.RedirectStandardError = true;
            //process.StartInfo.RedirectStandardInput = true;
            //String arg = " -i " + uri + " -acodec copy -vcodec copy " + path;
            Uri uri2;
            Uri uri3;

            if ((bool)Properties.Settings.Default.useManualCamUrl)
            {
                uri = new Uri("rtsp://" + Properties.Settings.Default.CamOneUrlManual + "/axis-media/media.amp");
                uri2 = new Uri("rtsp://" + Properties.Settings.Default.CamTwoUrlManual + "/axis-media/media.amp");
                uri3 = new Uri("rtsp://" + Properties.Settings.Default.CamThreeUrlManual + "/axis-media/media.amp");
            }
            else
            {
                uri = new Uri("rtsp://" + Properties.Settings.Default.CamOneUrl + "/axis-media/media.amp");
                uri2 = new Uri("rtsp://" + Properties.Settings.Default.CamTwoUrl + "/axis-media/media.amp");
                uri3 = new Uri("rtsp://" + Properties.Settings.Default.CamThreeUrl + "/axis-media/media.amp");
            }

            uri = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing
            uri2 = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing
            uri3 = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing

            String title = "MainWindow";
            String arg = " -i " + uri + " -i " + uri2 + " -i " + uri3 + " -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " -c:v 3M " + " -c copy -map 0 -vcodec copy -map 1 -vcodec copy -map 2 -vcodec copy -map 3 -vcodec h264 -preset ultrafast " + path;
            //String arg = " -i " + uri + " -i " + uri2 + " -i " + uri3 + " -c copy -map 0 -map 1 -map 2 " + path;
            //String arg = "-i rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4 -acodec copy -vcodec copy " + path;   // test RTSP stream
            process.StartInfo.FileName = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) +
                                    @"\ffmpeg\ffmpeg.exe";

            //recordInfo.Text += "\n arguments passed: " + arg;
            process.StartInfo.Arguments = arg;
            //process.StartInfo.WorkingDirectory = @"c:\tmp";

            process.StartInfo.UseShellExecute = false;
            //process.StartInfo.CreateNoWindow = true;

            process.EnableRaisingEvents = true;
            //process.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            //process.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);
            if (multiTrue)
            {
                process.Start();
                //process.BeginOutputReadLine();
                //process.BeginErrorReadLine();

                pID[0] = process.Id;
            }
            //////////////////////// Cam 2 ///////////////////////////////
            //uri2 = new Uri(@"C:\Users\Ken\Videos\avengers.m4v");
            path = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).FullName;
            //String mov = path + @"\Videos\sintel720p.mp4";
            //String mov2 = path + @"\Videos\avengers.mp4";

            //uri2 = new Uri(path +  @"\Videos\guardians720p.mov");
            String metaDataTitle = filename + " Flight Recording - " + Properties.Settings.Default.CompanyName;
            path += @"\" + filename + "cam1comb.mkv";
            cam2 = new Process();
            cam2.StartInfo.RedirectStandardOutput = true;
            cam2.StartInfo.RedirectStandardError = true;
            cam2.StartInfo.RedirectStandardInput = true;
            String camArg = "";
            String mapArg = "";
            int mapNum = 0;
            //Uri uri2 = new Uri("rtsp://192.168.2.200/axis-media/media.amp");
            //String title = "MainWindow";
            //arg = " -ss 1 -rtsp_transport tcp -i " + uri + " -ss 1 -rtsp_transport tcp -i " + uri2 + " -ss 1 -rtsp_transport tcp -i " + uri3 + " -ss 1 -r 30 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0 -vcodec copy -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec copy -map 2 -vcodec copy -map 3 -vcodec h264 -preset ultrafast " + path;  // arg = " -i " + uri + " -i " + uri2 + " -i " + uri3 + " -f gdigrab -framerate 5 -i title=" + "\"" + title + "\"" + " -b:v 3M " + " -c copy -map 0 -map 1 -map 2 -map 3 -vcodec h264 -preset ultrafast " + path;

            if (Properties.Settings.Default.useManualCamUrl)
            {
                if (Properties.Settings.Default.CamOneUrlManual != "" && Properties.Settings.Default.CamOneUrlManual.Length > 0)
                {
                    camArg += " -rtsp_transport tcp -i " + uri + " -ss 1";
                    mapArg += " -map " + mapNum;
                    mapNum++;
                }
                else
                    camArg += "";

                if (Properties.Settings.Default.CamTwoUrlManual != "" && Properties.Settings.Default.CamTwoUrlManual.Length > 0)
                {
                    camArg += " -rtsp_transport tcp -i " + uri2 + " -ss 1";
                    mapArg += " -map " + mapNum;
                    mapNum++;
                }
                else
                    camArg += "";

                if (Properties.Settings.Default.CamThreeUrlManual != "" && Properties.Settings.Default.CamThreeUrlManual.Length > 0)
                {
                    camArg += " -rtsp_transport tcp -i " + uri3 + " -ss 1";
                    mapArg += " -map " + mapNum;
                    mapNum++;
                }
                else
                    camArg += "";

                arg = camArg + " -r 30 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0 -vcodec copy -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec copy -map 2 -vcodec copy " + mapArg + " -vcodec h264 -preset ultrafast " + path;
            }
            else
                arg = " -ss 1 -rtsp_transport tcp -i " + uri + " -ss 1 -rtsp_transport tcp -i " + uri2 + " -ss 1 -rtsp_transport tcp -i " + uri3 + " -ss 1 -r 30 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0 -vcodec copy -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec copy -map 2 -vcodec copy -map 3 -vcodec h264 -preset ultrafast " + path;  // arg = " -i " + uri + " -i " + uri2 + " -i " + uri3 + " -f gdigrab -framerate 5 -i title=" + "\"" + title + "\"" + " -b:v 3M " + " -c copy -map 0 -map 1 -map 2 -map 3 -vcodec h264 -preset ultrafast " + path;

            cam2.StartInfo.FileName = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) +
                                    @"\ffmpeg\ffmpeg.exe";

            cam2.StartInfo.Arguments = arg;

            cam2.StartInfo.UseShellExecute = false;
            //cam2.StartInfo.CreateNoWindow = true;

            cam2.EnableRaisingEvents = true;
            cam2.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            cam2.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);
            if (!multiTrue)
            {
                cam2.Start();
                cam2.BeginOutputReadLine();
                cam2.BeginErrorReadLine();
                pID[1] = cam2.Id;
            }

            return pID;
        }

        static void OutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            //* Do your stuff with the output (write to console/log/StringBuilder)
            //textBox.AppendText(outLine.Data);
            Trace.WriteLine(outLine.Data);
            //main.recordInfo.AppendText(outLine.Data);
            camLog += outLine.Data;
        }

        static void stopRecordCamOne()
        {
            Trace.WriteLine(process.StandardOutput.ReadLine());
            if (process != null && process.TotalProcessorTime.Seconds > 0)
            {
                process.StandardInput.WriteLine("q");
                main.alertRecInfo("\n Recording Done!");
                main.alertRecInfo("Log: \n " + camLog);
            }
            //if (cam2 != null)
            //{
            //    cam2.StandardInput.WriteLine("q");
            //    main.alertRecInfo("\n Cam 2 Recording Done!");
            //    main.alertRecInfo("Log: \n " + camLog);
            //}
        }

        void alertRecInfo(String info)
        {
            recordInfo.Text += info + "\n";
        }



        private void stopRecord_Click(object sender, RoutedEventArgs e)
        {
            //recordInfo.Text += process.StandardOutput.ReadLine();
            stopRecordCamOne();
            //recordInfo.Text += process.StandardOutput.ReadLine();
        }
        //private void stopDebug_Click(object sender, RoutedEventArgs e)
        //{
        //    _showDebugInfo = !_showDebugInfo;
        //    if (_showDebugInfo && _continue)
        //        stopDebug.Background = Brushes.Green;
        //    else
        //        stopDebug.Background = Brushes.Red;
        //}

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (process != null)
                stopRecordCamOne();
            foreach (var process in Process.GetProcessesByName("ffmpeg"))
                process.Kill();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
        }

        private void camOneRec_Click(object sender, RoutedEventArgs e)
        {
            filename = DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "-";
            filename = filename.Replace('/', '-');
            filename = filename.Replace(':', '-');
            filename = filename.Replace(' ', '-');
            var uri = new Uri(_urlTextBox.Text);
            startRecordProcess(uri, false);

        }

        private void transferUSB(){
          string[] drives = Environment.GetLogicalDrives();
          foreach (string drive in drives)
          {
            try
            {
              DriveInfo di = new DriveInfo(drive);
              if (di.VolumeLabel == "ENTAIR")
              {
                
              }
            }
            catch
            {
              // ...
            }
          }

          foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.Removable)
            {
            Console.WriteLine(string.Format("({0}) {1}", drive.Name.Replace("\\",""), drive.VolumeLabel));
            }
        }           


        }
    }
}
