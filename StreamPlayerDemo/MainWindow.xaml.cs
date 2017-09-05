using System;
using System.Windows;
using System.IO.Ports;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using System.ComponentModel;
using System.Threading.Tasks;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Modules;
using System.Net.Http;
using System.Threading;

namespace CameraApplication
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        static SerialPort serialPort;
        static bool _continue = true;
        private NmeaParser.NmeaDevice gpsSerialDevice;
        private Queue<string> messages = new Queue<string>(60);     // was previously 101, but to cut back on RAM usage, it's now 60.
        SerialPort autoSerialPort = null;
        //int prcID;              // stores the process ID for the FFMPEG recording process.
        static MainWindow main; // allows for static functions to access local variables and UI elements
        static Process cam2;    // used for the FFMPEG recording process
        static CameraApplication.autoscanProgress prog; // initializes a progress window for the Autoscan(TM) function
        static String filename; // the local filename of the video (eg. 12-3-2017-6-12AM-FlightRecording)
        static string directorypath;    // the path of the program, eg. the install / run directory
        static string outputFile;   // the path to the output file
        //private Task _transferTask; // the task to transfer the video to a USB drive
        static bool useMotionJpg = true;    // flag to either use or not use MJPG video (instead of RTSP)
        private Process accel;      // accelerometer data process
        private int updateCtr = 0;  // used to average out accelerometer data
        static bool isStartRecording = true;
        static string camLog = "";
        private int avgPitch = 0;
        private int avgRoll = 0;
        private int avgCount = 0;
        private int prevAvgPitch = 0;   // store the previous averages to check to see if the current average makes sense
        private int prevAvgRoll = 0;
        public static bool isWebClientConnected = false;    // used for the webserver.  Unfortunately, the current system is unable to handle the load of the webserver, so for now this is disabled.
        public static WebSocketsChatServer chatServer = null;
        private int accelPitchOffset = 0;   // the offset calibration value fpr pitch calculated during the start while waiting for the cameras to start up.
        private int accelRollOffset = 0;    // the offset calibration value fpr roll calculated during the start while waiting for the cameras to start up.
        private bool accelCalibarationFlag = false; // true if the calibration has run
        static string temp = "";
        static bool isDownloadButtonPressed = false;
        //static bool isConnectedtoCamera = true;
        static bool isTransferComplete = false;

        private readonly BackgroundWorker worker = new BackgroundWorker()   // this worker is used for the initial startup
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        private readonly BackgroundWorker webWorker = new BackgroundWorker()    // this is used to start the webserver process, currently disabled.
        {
            WorkerReportsProgress = true,
            WorkerSupportsCancellation = true
        };

        public static object WebServer { get; internal set; }

        public MainWindow()
        {
            main = this;        // sets a static pointer to the current window, and allows for accessing non-static constructors from a static function
            InitializeComponent();  // initialize the WPF UI components
            _statusLabel.Text = "Connecting to serial port...";
            worker.DoWork += worker_DoWork; // set the worker to run the worker_DoWork function when called
            worker.RunWorkerCompleted += worker_RunWorkerCompleted; // assigns the worker runCompleted function

            if (Properties.Settings.Default.autoCheckGPSCommPort && !Properties.Settings.Default.useManualGPSComPort)  // if autoScan for serial ports is on, and use a manual port is off,
                //autoSerialPort = FindPort();
                worker.RunWorkerAsync();    // start the background scan for a serial port
            else
                serialFunc();               // else, just jump to initializing the serial stream for the GPS data

            if (prog != null)               // if the progress window is not null (it has been initialized) 
            {
                prog.Close();
                prog.Hide();
            }


            //////// This was disabled due to the lack of reliable data when flying; the bouncing of the plane would lead to inacurate readings
            //_statusLabel.Text = "Starting Accelerometer";
            //GetAccelerometerDataExe();  // start the exe proces from Lanner that connects to the built in accelerometer.  Todo: figure out how to dirrectly connect to the accelerometer, bypassing the current workaround solution.


            //// this commented section is for the webserver.  
            //// At the moment this is disabled, as the performance of the Lanner i7 box was not sufficient to run it
            ////webWorker.DoWork += webWorker_DoWork;

            //var url = "http://localhost:9696/";
            //var server = new WebServer(url);
            ////// If we want to enable sessions, we simply register the LocalSessionModule
            ////// Beware that this is an in-memory session storage mechanism so, avoid storing very large objects.
            ////// You can use the server.GetSession() method to get the SessionInfo object and manupulate it.
            ////// You could potentially implement a distributed session module using something like Redis
            ////server.RegisterModule(new LocalSessionModule());

            //string webpagePath = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) +
            //        @"\Webserver\";

            //// Here we setup serving of static files
            //server.RegisterModule(new StaticFilesModule(webpagePath));
            //// The static files module will cache small files in ram until it detects they have been modified.
            //server.Module<StaticFilesModule>().UseRamCache = true;
            //server.Module<StaticFilesModule>().DefaultExtension = ".html";
            //// We don't need to add the line below. The default document is always index.html.
            ////server.Module<Modules.StaticFilesWebModule>().DefaultDocument = "index.html";

            //// Once we've registered our modules and configured them, we call the RunAsync() method.
            //server.RegisterModule(new WebSocketsModule());
            //server.Module<WebSocketsModule>().RegisterWebSocketsServer<WebSocketsChatServer>("/info");

            //server.RunAsync();
            //recordInfo.Text += "Starting IO";

            var startDownloadTask = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                {
                    try
                    {
                        await client.GetAsync("http://192.168.1.200/axis-cgi/io/port.cgi?action=2%3A%5C"); // Turns off the download button light
                    }
                    catch { }
                }
            });

            if (Properties.Settings.Default.autoRecordOn)   // if the autoRecord setting is set to on
            {
                DelOldVideos();
                System.Threading.Thread.Sleep(90000);       // wait for the cameras to power up
                camRec_Click(this, new RoutedEventArgs());  // set up and start the record process
            }
            //serialFunc();
        }

        private void HandlePreviewButtonClick(object sender, RoutedEventArgs e)
        {
            CameraView cam = new CameraView();  // initializes a new preview window
            cam.Show();                         // shows the new window
        }



        private void DelOldVideos()
        {
            string path = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).FullName;
            //FileSystemInfo fileInfo = new DirectoryInfo(path).GetFileSystemInfos()
            //    .OrderBy(fi => fi.CreationTime).First();

            string[] files = Directory.GetFiles(path);

            try
            {
                foreach (string file in files)
                {
                    FileInfo fi = new FileInfo(file);
                    if (fi.CreationTime < DateTime.Now.AddDays(-7) && (fi.Extension.Contains("alx") || fi.Extension.Contains(".mkv")))
                        fi.Delete();
                }
            }
            catch { }

        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            // run all background tasks here
            //autoSerialPort = FindPort();
            IProgress<string> progress = null;

            var ports = System.IO.Ports.SerialPort.GetPortNames().OrderBy(s => s);
            if (ports.Count() <= 0)
            {
                Application.Current.Dispatcher.Invoke((Action)delegate  // the total amount of ports detected are 0, thus just give up and return
                {
                    _errorStatusLabel.Text += "Error 1: No Serial Ports Found"; // display the error in the status bar, so that it can be seem if a monitor is connected
                });

                _continue = false;
                autoSerialPort = null;
                return;
            }
            Application.Current.Dispatcher.Invoke((Action)delegate  // show the progress bar window
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

                    foreach (var baud in baudRatesToTest)      // test all detected serial ports, at each baud rate in order to try to find a NMEA GPS reciever
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
                                port.ReadTo("$GP"); // check for the NMEA signature, if it exists, then the port is likey connected to a GPS reciever that we can access
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
                            autoSerialPort = new System.IO.Ports.SerialPort(portName, baud);    // save the port for use later
                            return;
                        }
                    }
                }
            }

            autoSerialPort = null;  // else, something went wrong, and we return null
            Application.Current.Dispatcher.Invoke((Action)delegate
            {
                prog.Close();   // close the progress window, we are done here
                _errorStatusLabel.Text += "Error 2: Could not AutoDetect a GPS device";

            });
            return;

        }

        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e) // when the port scan is complete
        {
            if (serialPort != null)
                _statusLabel.Text = "Connected to serial port " + serialPort.PortName;  // if it worked, display a result
            else
                _statusLabel.Text = "Serial port connection failed";

            Trace.WriteLine("AutoSet worker finished"); // send some debug information to the output console
            Task.Delay(1000);                           // wait a second for the GPS to try to find a satelite / finish booting
            serialFunc();                               // start the serial initialization process
            worker.Dispose();                           // free up the memory / CPU used by this worker.
            //update ui once worker completes its work
        }

        private async void webIO()
        {
            temp = "";
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite);
                var tempData = "";
                var request = new HttpRequestMessage(HttpMethod.Get, "http://192.168.1.200/axis-cgi/io/port.cgi?checkactive=0");
                try
                {
                    using (var response = await client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead))
                    {
                        using (var body = await response.Content.ReadAsStreamAsync())
                        using (var reader = new StreamReader(body))
                            while (!reader.EndOfStream)
                            {
                                tempData = reader.ReadLine();
                                temp += tempData;
                                if (tempData.Contains("=active"))
                                {
                                    isDownloadButtonPressed = true;
                                    return;
                                }
                            }
                    }
                }
                catch
                {
                    Application.Current.Dispatcher.Invoke((Action)delegate
                    {
                        //recordInfo.Text += "\nIO State: Connection Failed!";
                        camLog += "\n || Error: IO State: Connection Failed! || ";
                    });

                    //isConnectedtoCamera = false;
                    return;
                }
            }
        }

        public void serialFunc()    // gets the serial port, and if it is a valid port, set up the gps parser
        {
            messages.Clear();
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
            Properties.Settings.Default.gpsCommPortName = serialPort.PortName;  // if autoset was enabled, save the found port
            Properties.Settings.Default.gpsComBaudRate = serialPort.BaudRate;
            Properties.Settings.Default.autoCheckGPSCommPort = false;       // disable after port is found to prevent autoscanning at every launch
            Properties.Settings.Default.Save();

            gpsSerialDevice = new NmeaParser.SerialPortDevice(serialPort);
            gpsSerialDevice.MessageReceived += gpsdevice_MessageReceived;
            gpsSerialDevice.OpenAsync();        // start the gos nmea parser
            _statusLabel.Text = serialPort.PortName + " Connected";

        }

        private void gpsdevice_MessageReceived(object sender, NmeaParser.NmeaMessageReceivedEventArgs args) // when gps data is recieved...
        {
            Dispatcher.BeginInvoke((Action)delegate ()
            {
                messages.Enqueue(args.Message.MessageType + ": " + args.Message.ToString());
                if (messages.Count > 50) messages.Dequeue(); //Keep message queue at 100
                if (args.Message is NmeaParser.Nmea.Gps.Gprmc)
                {
                    var msg = args.Message as NmeaParser.Nmea.Gps.Gprmc;
                    double speed = msg.Speed;

                    gpsDirectionVal.Text = msg.Course.ToString();       // get the gps bearing value
                    speedArc.EndAngle = ((speed / 200) * 240) - 120;    // calculate the angle for the 'speedometer' 
                    if (Properties.Settings.Default.speedInMeterSec)
                    {
                        speedVal.Text = ((int)(speed * 0.514444)).ToString();  // convert from knots to meters / second
                        speedUnitText.Text = "m/s";
                    }
                    else
                    {
                        speedVal.Text = ((int)(speed)).ToString();  // default is knots apparently
                        speedUnitText.Text = "Knots";
                    }
                }
                else if (args.Message is NmeaParser.Nmea.Gps.Gpgga) // this is a different line on the gps output with some different information
                {
                    gpggaView.Message = args.Message as NmeaParser.Nmea.Gps.Gpgga;  // set the gps information view (the blue box) data to the new message
                    if (Properties.Settings.Default.altitudeInMeeters)              // check to see if the altitude should be displayed in meters or feet
                    {
                        altitudeVal.Text = ((int)gpggaView.Message.Altitude).ToString();
                        altitudeUnit.Text = "Meters";  // gpggaView.Message.AltitudeUnits.ToString()
                    }
                    else
                    {
                        altitudeVal.Text = ((int)(gpggaView.Message.Altitude * 3.28084)).ToString();   // calculate feet from meters
                        altitudeUnit.Text = "Feet";
                    }
                }
                else
                {
                    // Do nothing
                }
            });
        }

        private void settingsBtn_Click(object sender, RoutedEventArgs e)
        {
            CameraApplication.Settings win2 = new CameraApplication.Settings(); // initialize the settings window
            win2.Show();                                                        // show the settings window
        }

        static int[] startRecordProcess()
        {
            isDownloadButtonPressed = false;

            //////////////////////// Merged Video ///////////////////////////////
            int[] pID = new int[3];
            string path = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).FullName;
            path += @"\" + filename + "output.mkv";

            String title = "MainWindow";
            String arg = "";
            Uri uri;
            Uri uri2;
            Uri uri3;

            //////////////////////// Cam 2 ///////////////////////////////
            //uri2 = new Uri(@"C:\Users\Ken\Videos\avengers.m4v");
            path = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).FullName; // get the path of the output directory (the user folder)

            String metaDataTitle = filename + " Flight Recording - " + Properties.Settings.Default.CompanyName; // Set the metadata of the output video
            path += @"\" + filename;

            cam2 = new Process();
            cam2.StartInfo.RedirectStandardOutput = true;   // set the process to redirect output to this program, so that we can use it
            cam2.StartInfo.RedirectStandardError = true;
            cam2.StartInfo.RedirectStandardInput = true;

            String camArg = ""; // the FFMPEG video recording arguments
            String mapArg = ""; // the FFMPEG video mapping arguments
            int mapNum = 0;     // mapped video counter

            //arg = " -ss 1 -rtsp_transport tcp -i " + uri + " -ss 1 -rtsp_transport tcp -i " + uri2 + " -ss 1 -rtsp_transport tcp -i " + uri3 + " -ss 1 -r 30 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0 -vcodec copy -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec copy -map 2 -vcodec copy -map 3 -vcodec h264 -preset ultrafast " + path;  // arg = " -i " + uri + " -i " + uri2 + " -i " + uri3 + " -f gdigrab -framerate 5 -i title=" + "\"" + title + "\"" + " -b:v 3M " + " -c copy -map 0 -map 1 -map 2 -map 3 -vcodec h264 -preset ultrafast " + path;

            if (Properties.Settings.Default.useManualCamUrl)    // if use manual urls is enabled, use those
            {
                if (Properties.Settings.Default.CamOneUrlManual != "" && Properties.Settings.Default.CamOneUrlManual.Length > 0)    // check to see if the manual URL has been set
                {
                    camArg += " -rtsp_transport tcp -i " + new Uri("rtsp://" + Properties.Settings.Default.CamOneUrlManual + "/axis-media/media.amp") + " ";
                    mapArg += " -map " + mapNum + " -vcodec copy ";
                    mapNum++;
                }
                else
                    camArg += "";

                if (Properties.Settings.Default.CamTwoUrlManual != "" && Properties.Settings.Default.CamTwoUrlManual.Length > 0)    // check to see if the manual URL has been set
                {
                    camArg += " -rtsp_transport tcp -i " + new Uri("rtsp://" + Properties.Settings.Default.CamTwoUrlManual + "/axis-media/media.amp") + " "; // -ss 1
                    mapArg += " -map " + mapNum + " -vcodec copy ";
                    mapNum++;
                }
                else
                    camArg += "";

                if (Properties.Settings.Default.CamThreeUrlManual != "" && Properties.Settings.Default.CamThreeUrlManual.Length > 0)    // check to see if the manual URL has been set
                {
                    camArg += " -rtsp_transport tcp -i " + new Uri("rtsp://" + Properties.Settings.Default.CamThreeUrlManual + "/axis-media/media.amp") + " ";
                    mapArg += " -map " + mapNum + " -vcodec copy ";
                    mapNum++;
                }
                else
                    camArg += "";

                if (mapArg.Trim().Length > 0)   // if any of the above urls were set
                    mapArg += " -map " + mapNum + " -vcodec h264 -preset ultrafast ";

                path += "manual.mkv";           // add manual.mkv to the end of the filename
                arg = " -report " + camArg + " -r 30 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy " + mapArg + " -shortest -metadata title=\"" + metaDataTitle + "\" " + path + " ";
                //arg = camArg + " -r 30 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy " + mapArg + " -metadata title=\"" + metaDataTitle + "\" " + path;
            }
            else if (Properties.Settings.Default.useAdjacentIPs)  // if the use adjacent IPs setting is enabled 
            {
                if (useMotionJpg)
                    arg = " -f dshow  -i audio=" + "\"Microphone (USB Audio Device)\"" + "  -r 24 -i " + new Uri("http://192.168.1.200/axis-cgi/mjpg/video.cgi?fps=24") + " -r 24 -i " + new Uri("http://192.168.1.201/axis-cgi/mjpg/video.cgi?fps=24") + " -r 30 -i " + new Uri("http://192.168.1.202/axis-cgi/mjpg/video.cgi?fps=30") + " -r 24 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec copy -map 4 -vcodec h264_qsv -preset fast " + path;
                else
                    arg = " -report -rtsp_transport tcp -i " + new Uri("rtsp://192.168.1.200/axis-media/media.amp") + " -rtsp_transport tcp -i " + new Uri("rtsp://192.168.1.202/axis-media/media.amp") + " -rtsp_transport tcp -i " + new Uri("rtsp://192.168.1.203/axis-media/media.amp") + " -r 30 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0 -vcodec copy -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec copy -map 2 -vcodec copy -map 3 -vcodec h264 -preset ultrafast " + path;  // arg = " -i " + uri + " -i " + uri2 + " -i " + uri3 + " -f gdigrab -framerate 5 -i title=" + "\"" + title + "\"" + " -b:v 3M " + " -c copy -map 0 -map 1 -map 2 -map 3 -vcodec h264 -preset ultrafast " + path;
            }
            else if (useMotionJpg && (bool)main.useIntelQSV.IsChecked)  // if the user is using the default urls, check to see if we should use MJPG and Intel's QSV
            {
                if ((bool)main.rec800p.IsChecked)       // check if we should record at 800p for all cameras (in case of CPU issues)
                {
                    path += "QSVmed.mkv";  // set the output name to make it obvious which mode was applied
                                                      // Resolution Options: 1280x1024, 1280x960, 1280x720, 768x576, 704x576, 704x480, 640x480, 640x360, 704x288, 704x240, 480x360, 384x288, 352x288, 352x240, 320x240, 240x180, 192x144, 176x144, 176x120, 160x120
                                                      //                 arg = "   -rtsp_transport tcp -r 25 -i " + new Uri("rtsp://" + Properties.Settings.Default.CamOneUrl + "/axis-media/media.amp?resolution=160x120") + " -i " + new Uri("http://192.168.1.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:1 -acodec aac -async 1 -map 1 -vcodec h264_qsv -preset fastest -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset fastest -map 3 -vcodec h264_qsv -preset fastest -map 4 -vcodec h264 -preset ultrafast " + path;
                                                      //arg = "  -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -i " + new Uri("http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -preset fastest -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset fastest -map 3 -vcodec h264_qsv -preset fastest -map 4 -vcodec h264 -preset ultrafast " + path;
                                                      //arg = "  -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -i " + "\"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15\"" + " -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset slow -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset slow -map 3 -vcodec h264_qsv -preset slow -map 4 -vcodec h264_qsv -preset fast " + path;

                    //arg = "  -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -r 25 -i " + "\"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15&fps=25\"" + " -r 25 -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&fps=25") + " -r 25 -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi?resolution=800x450&fps=25") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    //no sound vv
                    //arg = " -r 25 -i " + "\"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15\"" + " -r 25 -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi?resolution=1280x720") + " -r 25 -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0 -vcodec h264_qsv -look_ahead 0 -preset medium -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 2 -vcodec h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset fast " + path;

                    //arg = " -f dshow -i audio=" + "\"Microphone (Sound Blaster E1)\"" + " -r 25 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15\" -r 25 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi\" -r 25 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi\" -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    //arg = " -f dshow  -i audio=" + "\"Microphone (Sound Blaster E1)\"" + " -r 24 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15&fps=24\" -r 24 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    
                    //arg = " -f dshow  -i audio=" + "\"Microphone (USB Audio Device)\"" + " -r 24 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15&fps=24\" -r 24 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    arg = " -f dshow  -i audio=" + "\"Microphone (USB Audio Device)\"" + " -r 24 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15&fps=24\" -r 24 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;

                    // -rtbufsize 20M   - 24 fps ^^^ 
                    // ^ temp backup of previous settings with creative soundcard
                    //arg = " -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -r 25 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15\" -r 25 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi\" -r 25 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi\" -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    // with plane soundcard
                }
                else
                {
                    path += "QSVslow.mkv";       // don't use 800p for the cameras, and limit to the default settings chosen on the camera (720p, 640x800, 640x800)
                                                      // Resolution Options: 1280x1024, 1280x960, 1280x720, 768x576, 704x576, 704x480, 640x480, 640x360, 704x288, 704x240, 480x360, 384x288, 352x288, 352x240, 320x240, 240x180, 192x144, 176x144, 176x120, 160x120
                                                      //arg = "   -rtsp_transport tcp -r 25 -i " + new Uri("rtsp://" + Properties.Settings.Default.CamOneUrl + "/axis-media/media.amp?resolution=160x120") + " -i " + new Uri("http://192.168.1.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:1 -acodec aac -async 1 -map 1 -vcodec h264_qsv -preset fastest -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset fastest -map 3 -vcodec h264_qsv -preset fastest -map 4 -vcodec h264 -preset ultrafast " + path;
                                                      //arg = "  -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -i " + new Uri("http://192.168.1.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -preset fastest -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset fastest -map 3 -vcodec h264_qsv -preset fastest -map 4 -vcodec h264 -preset ultrafast " + path;
                                                      //arg = " -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" -r 25 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15\" -r 25 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi\" -r 25 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi\" -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset slow -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset slow -map 3 -vcodec h264_qsv -preset slow -map 4 -vcodec h264_qsv -preset fast " + path;

                    //actual qsv:
                    //arg = "  -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -r 25 -i " + "\"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15&fps=25\"" + " -r 25 -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&fps=25") + " -r 25 -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi?resolution=800x450&fps=25") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset slow -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset slow -map 3 -vcodec h264_qsv -preset slow -map 4 -vcodec h264_qsv -preset fast " + path;

                    //arg = " -f dshow -i audio=" + "\"Microphone (Sound Blaster E1)\"" + " -r 25 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15\" -r 25 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi\" -r 25 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi\" -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset slow -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    //arg = " -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -r 25 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15\" -r 25 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi\" -r 25 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi\" -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset slow -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    //arg = " -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -r 25 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15\" -r 25 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi\" -r 25 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi\" -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset slow -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;


                    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    //
                    arg = " -itsoffset -02.000 -f dshow  -i audio=" + "\"Microphone (USB Audio Device)\"" + " -r 24 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15&fps=24\" -r 24 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    //arg = " -itsoffset -02.000 -f dshow  -i audio=" + "\"Microphone (Sound Blaster E1)\"" + " -r 24 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15&fps=24\" -r 24 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi?fps=24\" -r 24 -f gdigrab -framerate 1 -draw_mouse 0 -i title=\"" + title + "\"  -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264_qsv -look_ahead 0 -preset medium -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset medium -map 3 -vcodec h264_qsv -preset medium -map 4 -vcodec h264_qsv -preset fast " + path;
                    // This version works the best overall, blending speed, fps and quality.
                    /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


                    //arg = "-r 25 -i \"http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=1280x720&compression=15&fps=25\" -r 25 -i \"http://192.168.2.200/axis-cgi/mjpg/video.cgi?fps=25\" -r 25 -i \"http://192.168.3.200/axis-cgi/mjpg/video.cgi?fps=25\" -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " -c copy -map 0 -vcodec h264_qsv -look_ahead 0 -preset slow -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264_qsv -preset slow -map 2 -vcodec h264_qsv -preset slow -map 3 -vcodec h264_qsv -preset fast " + path;
                    // No sound ^^^
                    // more info avaliable here: https://forum.videohelp.com/threads/382511-quicksync-via-ffmpeg
                }
            }
            else if (useMotionJpg && !Properties.Settings.Default.useAdjacentIPs)  // Add in -report at the start of the command for more information
            {
                if ((bool)main.rec800p.IsChecked)
                {
                    path += "MPJG-800p.mkv";
                    arg = " -itsoffset -02.000 -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -i " + new Uri("http://192.168.1.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi?resolution=800x450") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264 -preset ultrafast -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264 -preset ultrafast -map 3 -vcodec h264 -preset ultrafast -map 4 -vcodec h264 -preset ultrafast " + path;
                }
                else
                {
                    path += "MPJG.mkv";
                    //arg = "   -rtsp_transport tcp -r 25  -itsoffset -02.000 -i " + new Uri("rtsp://" + Properties.Settings.Default.CamOneUrl + "/axis-media/media.amp?resolution=160x120") + " -i " + new Uri("http://192.168.1.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:1 -acodec copy -async 1 -map 1 -vcodec h264 -preset ultrafast -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264 -preset ultrafast -map 3 -vcodec h264 -preset ultrafast -map 4 -vcodec h264 -preset ultrafast " + path;
                    arg = " -itsoffset -02.000 -f dshow -i audio=" + "\"Microphone (USB Audio Device)\"" + " -i " + new Uri("http://192.168.1.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.2.200/axis-cgi/mjpg/video.cgi") + " -i " + new Uri("http://192.168.3.200/axis-cgi/mjpg/video.cgi") + " -r 25 -f gdigrab -framerate 1 -draw_mouse 0 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0:0 -acodec copy -async 1 -map 1 -vcodec h264 -preset ultrafast -map 2 -metadata title=\"" + metaDataTitle + "\" -vcodec  h264 -preset ultrafast -map 3 -vcodec h264 -preset ultrafast -map 4 -vcodec h264 -preset ultrafast " + path;
                }
            }
            else
            {
                uri = new Uri("rtsp://" + Properties.Settings.Default.CamOneUrl + "/axis-media/media.amp");
                uri2 = new Uri("rtsp://" + Properties.Settings.Default.CamTwoUrl + "/axis-media/media.amp");
                uri3 = new Uri("rtsp://" + Properties.Settings.Default.CamThreeUrl + "/axis-media/media.amp");

                //uri = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing
                //uri2 = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing
                //uri3 = new Uri("rtsp://mpv.cdn3.bigCDN.com:554/bigCDN/_definst_/mp4:bigbuckbunnyiphone_400.mp4");  // for testing

                path += "RTSP.mkv";
                arg = " -rtsp_transport udp -i \"rtsp://root:pass@192.168.1.200/axis-media/media.amp?videocodec=h264\" " + " -rtsp_transport udp -i \"rtsp://root:pass@192.168.2.200/axis-media/media.amp?videocodec=h264\" " + " -rtsp_transport udp -i \"rtsp://root:pass@192.168.3.200/axis-media/media.amp?videocodec=h264\" " + " -r 30 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0 -vcodec copy -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec copy -map 2 -vcodec copy -map 3 -vcodec h264 -preset ultrafast " + path;
                //arg = " -rtsp_transport tcp -i " + uri + " -rtsp_transport tcp -i " + uri2 + " -rtsp_transport tcp -i " + uri3 + " -r 30 -f gdigrab -framerate 1 -i title=" + "\"" + title + "\"" + " " + " -c copy -map 0 -vcodec copy -map 1 -metadata title=\"" + metaDataTitle + "\" -vcodec copy -map 2 -vcodec copy -map 3 -vcodec h264 -preset ultrafast " + path;  // arg = " -i " + uri + " -i " + uri2 + " -i " + uri3 + " -f gdigrab -framerate 5 -i title=" + "\"" + title + "\"" + " -b:v 3M " + " -c copy -map 0 -map 1 -map 2 -map 3 -vcodec h264 -preset ultrafast " + path;
            }

            camLog += "\nRecording Arguments: " + arg + "\n\n";

            directorypath = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);  // get the current directory of the running program
            cam2.StartInfo.FileName = directorypath + @"\ffmpeg\ffmpeg.exe";        // find the bundled FFMPEG executable and run it

            cam2.StartInfo.Arguments = arg;     // set the runtime arguments 

            cam2.StartInfo.UseShellExecute = false; // use the default execution, not the admin shell
            cam2.StartInfo.CreateNoWindow = true;   // do not display a window for the process

            cam2.EnableRaisingEvents = true;        // enable and set the event handlers for the process
            cam2.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            cam2.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);
            cam2.Exited += (s, e) =>
            {
                isStartRecording = false;
                main.Dispatcher.Invoke(() =>
                {
                    main._statusLabel.Text = "Recording Finished";
                    isDownloadButtonPressed = true;
                    main.downloadButtonPressed();
                });
            };

            cam2.Start();               // start recording
            cam2.BeginOutputReadLine(); // start reading the output
            cam2.BeginErrorReadLine();  // start reading the error output and catch any errors thrown
            pID[1] = cam2.Id;           // save the process id
            outputFile = path;          // set the output filepath for later
            isStartRecording = true;    // set the has the recording process started flag to true (while this flag is true, the process itself has started, but recording has not.  It's currently in limbo waiting for the camera streams to connect)

            var startDownloadTask = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                {
                    try
                    {
                        await client.GetAsync("http://192.168.1.200/axis-cgi/io/port.cgi?action=2%3A%2F"); // Turns on the download button light
                    }
                    catch { }
                }
            });

            var loop1Task = Task.Run(async () =>
            {
                while (!isDownloadButtonPressed)
                {
                    main.webIO();
                    await Task.Delay(200);
                }
                Application.Current.Dispatcher.Invoke((Action)delegate
                        {
                            //main.recordInfo.Text += "\nIO State: \n" + temp + " downloadButtonPressed? " + isDownloadButtonPressed;
                            stopRecordCamTwo();
                            main._statusLabel.Text = "Stopping Recording";
                        });
                //main.downloadButtonPressed();

            });

            main.Dispatcher.Invoke(() =>
            {
                main._statusLabel.Text = "Preparing to Record";     // output the current recording status to the window
                //if (isWebClientConnected && chatServer != null)   // if the webserver is enabled, send the information to the /cmd page
                //    chatServer.sendWebInfo("Preparing to Record Video");
            });


            return pID;
        }

        static void OutputHandler(object sendingProcess, DataReceivedEventArgs outLine) // handle the output of the FFMPEG process
        {
            //textBox.AppendText(outLine.Data);
            //Trace.WriteLine(outLine.Data);
            //main.recordInfo.AppendText(outLine.Data);
            if (isStartRecording)   // if the recording proccess has started, but the actual recording has not yet started
                try
                {
                    if (outLine.Data.Contains("frame="))    // check to see if FFMPEG has started to record
                    {
                        main.Dispatcher.Invoke(() =>
                        {
                            main._statusLabel.Text = "Recording";
                            //if (isWebClientConnected && chatServer != null)
                            //    chatServer.sendWebInfo("Recording");
                        });
                        isStartRecording = false;           // recording has started, set the waiting to record flag to false

                    }
                }
                catch { }
            camLog += outLine.Data + "\n";
        }

        static void stopRecordCamTwo()  // stop the current recording 
        {
            if (cam2 != null)
            {
                try
                {
                    //Trace.WriteLine(cam2.StandardOutput.ReadLine());
                    cam2.StandardInput.WriteLine("q");  // write "q" to stdin in order to end the FFMPEG recording process, and properly save the video file
                    /// For debugging purposes
                    //main.alertRecInfo("\n Recording Done!");
                    //if (isWebClientConnected && chatServer != null)
                    //{
                    //    chatServer.sendWebInfo("Recording Done");     // if the webserver is enabled, output the result
                    //    chatServer.sendWebInfo("Recording Log: " + camLog);
                    //}
                    //main.alertRecInfo("Log: \n " + camLog);
                }
                catch
                {

                }
                if (isStartRecording == true)   // if FFMPEG is still waiting to start recording, just kill the process
                    cam2.Kill();
            }
            isStartRecording = false;   // set the FFMPEG processing status to false

        }

        void alertRecInfo(String info)  // display debug recording information to the recordInfo textbox
        {
            //recordInfo.Text += info + "\n";
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) // if the progrma is being closed...
        {
            //if (process != null)
            //    stopRecordCamOne();
            try
            {
                stopRecordCamTwo();
            }
            catch { }

            foreach (var process in Process.GetProcessesByName("ffmpeg"))   // make sure all FFMPEG processes are killed, if currently recording, stop the recording process
                process.Kill();
            try
            {
                accel.Kill();   // kill the accelerometer process if it has been started
            }
            catch
            {
                foreach (var process in Process.GetProcessesByName("adxl345"))  // if not, try killing any process with the name adxl345 anyways, in the event that it was running, but the PID changed
                    process.Kill();
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
        }

        private void camRec_Click(object sender, RoutedEventArgs e) // on the click of the start recording button...
        {
            filename = DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + "-";
            filename = filename.Replace('/', '-');
            filename = filename.Replace(':', '-');
            filename = filename.Replace(' ', '-');
            startRecordProcess();

        }

        private async void flashDownloadButton()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    await client.GetAsync("http://192.168.1.200/axis-cgi/io/port.cgi?action=2%3A%2F300%5C300%2F300%5C300%2F300%5C300%2F300%5C300%2F"); // blinks a bunch of times then stops with a solid light
                }
            }
            catch (Exception e)
            {
                main.Dispatcher.Invoke(() =>
                {
                    camLog += "\nPulse 1 Failed: Error: " + e.Message + "\n" + e.Source + "\n" + e.StackTrace;
                    //recordInfo.Text += "\nPulse 1 Failed: Error: " + e.Message + "\n" + e.Source + "\n" + e.StackTrace;
                });
                isTransferComplete = true;
                return;
            }
        }
        private async void downloadButtonPressed()
        {
            await gpsSerialDevice.CloseAsync();
            accel.Close();

            var startDownloadTask = Task.Run(async () =>
            {
                while (!isTransferComplete)
                {
                    main.flashDownloadButton();
                    await Task.Delay(100);
                }
            });

            //recordInfo.Text += "transfering started...";
            bool transferSuccess = transferUSB();
            isTransferComplete = true;

            var endDownloadTask = Task.Run(async () =>
            {
                using (var client = new HttpClient())
                {
                    try
                    {
                        await client.GetAsync("http://192.168.1.200/axis-cgi/io/port.cgi?action=2%3A%5C"); // Turns off the download button light
                    }
                    catch (Exception e)
                    {
                        main.Dispatcher.Invoke(() =>
                        {
                            camLog += "\nPulse 2 Failed: Error: " + e.Message + "\n" + e.Source + "\n" + e.StackTrace;
                            //recordInfo.Text += "Pulse Log: " + test;
                        });
                        return;
                    }
                }
            });
            //recordInfo.Text += "transfer complete";

            if (transferSuccess)
            {
                main.Window_Closing(new object(), new CancelEventArgs());
                System.Windows.Application.Current.Shutdown();
            }
        }

        private void stopCamRec_Click(object sender, RoutedEventArgs e) // stop recording button handler
        {
            stopRecordCamTwo();
            downloadButtonPressed();
        }

        private bool transferUSB()    // transfers the video file to a USB memory stick
        {
            string output = "";         // debugging output log
            bool finished = false;

            foreach (DriveInfo drive in DriveInfo.GetDrives())  // for each drive connected to the computer,
            {
                if (drive.DriveType == DriveType.Removable)     // check if it a removable drive (so that we don't accidentally get an internal hard drive)
                {
                    Trace.WriteLine(string.Format("({0}) {1}", drive.Name.Replace("\\", ""), drive.VolumeLabel));   // write the drive info to the debug log
                    output += string.Format("({0}) {1}", drive.Name.Replace("\\", ""), drive.VolumeLabel);          // write it again to the output log
                    if (drive.IsReady)      // check to see if the drive is ready to write to
                    {
                        DirectoryInfo dir = drive.RootDirectory;    // if it is, set the variable dir to the root directory of the drive
                        dir.CreateSubdirectory("Flight Videos");    // create a new folder on the drive if it does not already exist
                        FileInfo video = null;                      // set a new variable with the file information to null
                        try
                        {
                            video = new FileInfo(outputFile);       // try setting video to the output file from the recording process
                            var startTime = DateTime.Now;
                            if ((bool)useIntelQSV.IsChecked)        // if QSV was used, rename the output to .alx, as this will require different handleing on video playback
                                video.CopyTo(drive.RootDirectory.FullName + @"Flight Videos\" + video.Name.Substring(0, video.Name.Length - 3) + "alx", true);
                            else
                                video.CopyTo(drive.RootDirectory.FullName + @"Flight Videos\" + video.Name, true);  // else, keep the original name and copy the video to the drive


                            finished = true;

                            var endTime = DateTime.Now;
                            var total = endTime - startTime;
                            camLog += "\ncopy complete! Finished at: " + System.DateTime.Now + " taking " + total.TotalSeconds + " seconds -> " + total + " copied to: " + drive.Name + "\n";
                            File.WriteAllText(drive.RootDirectory.FullName + @"Flight Videos\" + video.Name + ".log", camLog);
                            return finished;
                        }
                        catch (Exception e)   // the file transfer failed, show an error and end the process
                        {
                            //                     MessageBox.Show(
                            //    "Error: Video transfer failed: no file found. \n"
                            //    , "Error 6: File transfer failed",
                            //MessageBoxButton.OK,
                            //MessageBoxImage.Error);
                            camLog += "\n || No file found " + e.Message + " || ";
                        }

                    }
                    else
                        camLog += "\nDrive is not Ready!";  // the drive was not ready for whatever reason, and the copy failed.

                }
            }

            //return output;  // return the output log
            return finished;

        }

        private void transferGarbageDataTest()  // transfers a file of garbage data to the usb in order to do a speed test.
        {                                       // this function transfers a file called test.temp from the user directory to the USB drive
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                string output = "";
                if (drive.DriveType == DriveType.Removable)
                {
                    output += string.Format("({0}) {1}", drive.Name.Replace("\\", ""), drive.VolumeLabel);
                    if (drive.IsReady)
                    {
                        drive.RootDirectory.CreateSubdirectory("Flight Videos");
                        FileInfo video = null;
                        try
                        {
                            string path = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)).FullName;
                            path += @"\" + "test.temp";
                            video = new FileInfo(path);
                            Trace.WriteLine("Starting record at: " + System.DateTime.Now);
                            var startTime = DateTime.Now;
                            video.CopyTo(drive.RootDirectory.FullName + @"Flight Videos\" + video.Name, true);
                            var endTime = DateTime.Now;
                            var total = endTime - startTime;
                            Trace.WriteLine("copy complete! Finished at: " + System.DateTime.Now + " taking " + total.TotalSeconds + " seconds " + total);
                            output += "copy complete! Finished at: " + System.DateTime.Now + " taking " + total.TotalSeconds + " seconds " + total;
                        }
                        catch
                        {
                            output += "No file found";
                        }

                    }
                    else
                        output += "\nDrive is not Ready!";
                    return;
                }
            }

        }

        private void useMotionJpgChk_Click(object sender, RoutedEventArgs e)    // set useMotionJpg to the state of the checkbox
        {
            useMotionJpg = (bool)useMotionJpgChk.IsChecked;
        }

        private void GetAccelerometerDataExe()  // launches the adxl345.exe process and gets the acelerometer data from it
        {

            accel = new Process();
            accel.StartInfo.RedirectStandardOutput = true;
            accel.StartInfo.RedirectStandardError = true;
            accel.StartInfo.RedirectStandardInput = true;

            accel.StartInfo.FileName = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName) +
                        @"\ffmpeg\adxl345.exe";


            accel.StartInfo.UseShellExecute = false;
            accel.StartInfo.CreateNoWindow = true;

            accel.EnableRaisingEvents = true;
            accel.OutputDataReceived += new DataReceivedEventHandler(AccelOutputHandler);
            accel.ErrorDataReceived += new DataReceivedEventHandler(AccelOutputHandler);
            accel.Exited += (s, e) =>
            {
                main.Dispatcher.Invoke(() =>
                {
                    accelerometerData.Text = "N/A";
                    main._statusLabel.Text = "Accelerometer Connection Failed";
                });
            };

            accel.Start();
            accel.BeginOutputReadLine();
            accel.BeginErrorReadLine();
            main._statusLabel.Text = "";
        }

        private void AccelOutputHandler(object sender, DataReceivedEventArgs e) // handles the output dtaa from the accelerometer
        {
            if (!accelCalibarationFlag) // if the calibration has not been completed
            {
                while (updateCtr < 10)  // toss out the first few numbers, these tend to not be accurate
                {
                        updateCtr++;
                }
                updateCtr = 0;

                while (updateCtr < 50)  // while it has been less than 40 lines of outputed (usable) data recieved
                {
                    if (updateCtr % 5 == 0) // if the counter mod 5 is 0, try parsing the recieved data 
                    {
                        string data = "";
                        try
                        {
                            data = e.Data.Substring(2); // get the data from the acceleromer, throw away the first two chars
                            data.Trim();                // remove any unnecesarry punctuation or spaces
                        }
                        catch
                        {
                            return;
                        }
                        int x = 0;
                        int y = 0;
                        double xData = 0;
                        double yData = 0;

                        if (data.Length < 5)   // if the data we got was blank, return
                            return;


                        try
                        {
                            Double.TryParse(data.Substring(0, 5), out xData);
                            data = data.Substring(data.IndexOf("Y=") + 2);
                            Double.TryParse(data.Substring(0, 5), out yData);
                            x = (int)(100 * xData); // multiply by 100 to remove the need to use a double, this helpes with memory / cpu usage
                            y = (int)(-100 * yData);

                            if (Math.Abs(x) > 80 || Math.Abs(y) > 80) // if the recieved value is greater than the absolute value of 55, throw it away for calibration purposes we assume the tilt angle is never greater than ~80 degrees from flat
                            {
                                return;
                            }
                            accelPitchOffset += x;  // add the calculated value to the offset calculation
                            accelRollOffset += y;
                            updateCtr++;
                        }
                        catch
                        {
                            return;
                        }

                    }
                    else
                        updateCtr++;
                }

                accelPitchOffset = accelPitchOffset / 11;   // divide the total value by 11, this is the offset for the pitch
                accelRollOffset = accelRollOffset / 11;     // divide the total value by 11, this is the offset for the roll
                accelCalibarationFlag = true;               // we have finished calibration of the accelerometer, so set the flag to true
                updateCtr = 0;                              // reset the update counter to 0
                camLog += "\n Accelerometer Calibration Report: \n Pitch offset: " + accelPitchOffset + " || Roll Offset: " + accelRollOffset + "\n";
            }

            if (updateCtr % 5 == 0)   // was %5, > 15       // if the update counter mod 4 is 0, this is to reduce the number of times the cpu has to go through this function
            {
                //Trace.WriteLine(e.Data);

                string data = "";
                try
                {
                    data = e.Data.Substring(2);             // get the data from the exe output
                    data.Trim();                            // remove any spaces
                }
                catch
                {
                    return;
                }
                int x = 0;
                int y = 0;
                double xData = 0;
                double yData = 0;
                //double z = 0;  // removed for now, was not all that useful as it only gave an indication of which way was up

                if (data.Length > 0)
                {
                    try
                    {
                        Double.TryParse(data.Substring(0, 5), out xData);   // try to parse the data into a usable number
                        data = data.Substring(data.IndexOf("Y=") + 2);
                        Double.TryParse(data.Substring(0, 5), out yData);
                        //data = data.Substring(data.IndexOf("Z=") + 2);
                        //Double.TryParse(data.Substring(0, 5), out z);
                        x = (int)(100 * xData);
                        y = (int)(-100 * yData);

                        if (Math.Abs(x) > 125 || Math.Abs(y) > 125)     // if the recieved value is greater than the absolute value of 125, throw it away, it seems to not be useful (numbers >500 were randomly seen)
                        {
                            return;
                        }

                        avgPitch += x;
                        avgRoll += y;
                        avgCount++;
                        //z = -z * 100; // Note: the sensor is upside down, so a Z value of -1 means the box is right side up
                    }
                    catch
                    {
                        return;
                    }
                    if (updateCtr <= 30)
                    {
                        updateCtr++;
                        return;
                    }
                    else
                    {
                        y = avgRoll / avgCount;     // calculate the average roll over the collection period
                        x = avgPitch / avgCount;    // calculate the average pitch over the collection period
                        y -= accelRollOffset;       // subtract the offset from the calculated value
                        x -= accelPitchOffset;

                        if (prevAvgPitch != avgPitch && prevAvgRoll != avgRoll) // if the previous pitch and roll are not equal to the new values
                            this.Dispatcher.Invoke(() =>
                            {
                                //accelerometerData.Text = e.Data;
                                xAxisBar.X2 = x;    // set the pitch bar end point to be equal to the calculated average pitch
                                yAxisBar.X2 = y;    // set the roll bar end point to be equal to the calculated average roll
                                                    //zAxisBar.X2 = z;
                                                    //recordInfo.Text += "\nAvg Roll: " + avgRoll + " avgPitch: " + avgPitch + " -> Calculated x: " + x + " Y: " + y + " | data x: " + xNums[0] + " " + xNums[1] + " " + xNums[2] + " " + xNums[3] + " y: " + yNums[0] + " " + yNums[1] + " " + yNums[2] + " " + yNums[3];

                                if (Math.Abs(x) > 5 && Math.Abs(x) <= 10)  // x is pitch (blame Lanner :/  )
                                    xAxisBar.Stroke = Brushes.Turquoise;    // set the bar colors and draw the pitch and roll bars
                                else if (Math.Abs(x) > 10 && Math.Abs(x) <= 15)
                                    xAxisBar.Stroke = Brushes.Yellow;
                                else if (Math.Abs(x) > 15 && Math.Abs(x) <= 20)
                                    xAxisBar.Stroke = (SolidColorBrush)(new BrushConverter().ConvertFrom("#ffbf49"));   // orange
                                else if (Math.Abs(x) > 20)
                                    xAxisBar.Stroke = (SolidColorBrush)(new BrushConverter().ConvertFrom("#fd4855"));   // dark red
                                else if (Math.Abs(x) <= 5)
                                    xAxisBar.Stroke = (SolidColorBrush)(new BrushConverter().ConvertFrom("#5cf145"));   // green

                                if (Math.Abs(y) > 10 && Math.Abs(y) <= 20)      // y is roll 
                                    yAxisBar.Stroke = Brushes.Turquoise;
                                else if (Math.Abs(y) > 20 && Math.Abs(y) <= 30)
                                    yAxisBar.Stroke = Brushes.Yellow;
                                else if (Math.Abs(y) > 30 && Math.Abs(y) <= 45)
                                    yAxisBar.Stroke = (SolidColorBrush)(new BrushConverter().ConvertFrom("#ffbf49"));   // orange
                                else if (Math.Abs(y) > 45)
                                    yAxisBar.Stroke = (SolidColorBrush)(new BrushConverter().ConvertFrom("#fd4855"));   // red
                                else if (Math.Abs(y) <= 10)
                                    yAxisBar.Stroke = (SolidColorBrush)(new BrushConverter().ConvertFrom("#5cf145"));   // green

                                accelerometerData.Text = "Pitch: " + x + "°\nRoll: " + y + "°";  // Note: x and y are flipped on the accelerometer :/
                            }); // else, there is no need to update the values shown, just skip the process
                        updateCtr = 0;      // reset all of the local variables
                        prevAvgRoll = avgRoll;
                        prevAvgPitch = avgPitch;
                        avgPitch = 0;
                        avgRoll = 0;
                        avgCount = 0;
                    }
                }
            }
            else
                updateCtr++;
        }

    }
}
