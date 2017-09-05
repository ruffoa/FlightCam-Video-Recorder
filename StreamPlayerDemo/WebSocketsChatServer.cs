using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unosquare.Swan;
using System.Threading;
using System.Net.WebSockets;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Modules;

namespace CameraApplication
{
    
    /// <summary>
    /// Defines a very simple chat server
    /// </summary>
    [WebSocketHandler("/information")]
    public class WebSocketsChatServer : WebSocketsServer
    {
        WebSocketContext webContext = null;
        
        public WebSocketsChatServer()
            : base(true, 0)
        {
            MainWindow.chatServer = this;
            // placeholder
        }

        /// <summary>
        /// Called when this WebSockets Server receives a full message (EndOfMessage) form a WebSockets client.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="rxBuffer">The rx buffer.</param>
        /// <param name="rxResult">The rx result.</param>
        protected override void OnMessageReceived(WebSocketContext context, byte[] rxBuffer, WebSocketReceiveResult rxResult)
        {
            foreach (var ws in WebSockets)
            {

                if (ws != context)
                    Send(ws, Encoding.UTF8.GetString(rxBuffer));
            }
        }

        /// <summary>
        /// Gets the name of the server.
        /// </summary>
        /// <value>
        /// The name of the server.
        /// </value>
        public override string ServerName => "Camera System Web Server";

        /// <summary>
        /// Called when this WebSockets Server accepts a new WebSockets client.
        /// </summary>
        /// <param name="context">The context.</param>
        protected override void OnClientConnected(WebSocketContext context)
        {
            Send(context, "Welcome to the camera system webserver!");
            MainWindow.isWebClientConnected = true;
            webContext = context;
            foreach (var ws in WebSockets)
            {
                if (ws != context)
                    Send(ws, "Someone else has logged in.");
            }
        }

        public void sendWebInfo(string info)
        {
            foreach (var ws in WebSockets)
            {
                    Send(ws, info);
            }

        }
        /// <summary>
        /// Called when this WebSockets Server receives a message frame regardless if the frame represents the EndOfMessage.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="rxBuffer">The rx buffer.</param>
        /// <param name="rxResult">The rx result.</param>
        protected override void OnFrameReceived(WebSocketContext context, byte[] rxBuffer, WebSocketReceiveResult rxResult)
        {
            return;
        }

        /// <summary>
        /// Called when the server has removed a WebSockets connected client for any reason.
        /// </summary>
        /// <param name="context">The context.</param>
        protected override void OnClientDisconnected(WebSocketContext context)
        {
            //Broadcast("Someone else has logged out.");  // not sure if this is neccesary 
        }
    }


    /////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// Define a command-line interface terminal
    /// </summary>
    [WebSocketHandler("/terminal")]
    public class WebSocketsTerminalServer : WebSocketsServer
    {

        // we'll keep track of the processes here
        private readonly Dictionary<WebSocketContext, Process> Processes = new Dictionary<WebSocketContext, Process>();
        // The SyncRoot is used to send 1 thing at a time and multithreaded Processes dictionary.
        private readonly object SyncRoot = new object();

        /// <summary>
        /// Called when this WebSockets Server receives a full message (EndOfMessage) form a WebSockets client.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="rxBuffer">The rx buffer.</param>
        /// <param name="rxResult">The rx result.</param>
        protected override void OnMessageReceived(WebSocketContext context, byte[] rxBuffer, WebSocketReceiveResult rxResult)
        {
            lock (SyncRoot)
            {
                var arg = Encoding.UTF8.GetString(rxBuffer);
                Processes[context].StandardInput.WriteLine(arg);
            }
        }

        /// <summary>
        /// Called when this WebSockets Server receives a message frame regardless if the frame represents the EndOfMessage.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="rxBuffer">The rx buffer.</param>
        /// <param name="rxResult">The rx result.</param>
        protected override void OnFrameReceived(WebSocketContext context, byte[] rxBuffer, WebSocketReceiveResult rxResult)
        {
            // don't process partial frames
            return;
        }

        /// <summary>
        /// Finds the context given the process.
        /// </summary>
        /// <param name="p">The p.</param>
        /// <returns></returns>
        private WebSocketContext FindContext(Process p)
        {
            lock (SyncRoot)
            {
                foreach (var kvp in Processes)
                {
                    if (kvp.Value == p)
                        return kvp.Key;
                }
            }

            return null;
        }

        /// <summary>
        /// Called when this WebSockets Server accepts a new WebSockets client.
        /// </summary>
        /// <param name="context">The context.</param>
        protected override void OnClientConnected(WebSocketContext context)
        {
            var process = new Process()
            {
                EnableRaisingEvents = true,
                StartInfo = new ProcessStartInfo()
                {
                    CreateNoWindow = true,
#if !NETCOREAPP1_1
                    ErrorDialog = false,
#endif
                    FileName = "cmd.exe",
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WorkingDirectory = "c:\\"
                }
            };

            process.OutputDataReceived += (s, e) =>
            {
                lock (SyncRoot)
                {
                    if ((s as Process).HasExited) return;
                    var ws = FindContext(s as Process);
                    if (ws != null && ws.WebSocket.State == WebSocketState.Open)
                        Send(ws, e.Data);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                lock (SyncRoot)
                {
                    if ((s as Process).HasExited) return;
                    var ws = FindContext(s as Process);
                    if (ws != null && ws.WebSocket.State == WebSocketState.Open)
                        Send(ws, e.Data);
                }
            };

            process.Exited += (s, e) =>
            {
                lock (SyncRoot)
                {
                    var ws = FindContext(s as Process);
                    if (ws != null && ws.WebSocket.State == WebSocketState.Open)
                        ws.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Process exited", CancellationToken.None).GetAwaiter().GetResult();
                }
            };

            // add the process to the context
            lock (SyncRoot)
            {
                Processes[context] = process;
            }

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

        }

        /// <summary>
        /// Called when the server has removed a WebSockets connected client for any reason.
        /// </summary>
        /// <param name="context">The context.</param>
        protected override void OnClientDisconnected(WebSocketContext context)
        {
            lock (SyncRoot)
            {
                if (Processes[context].HasExited == false)
                    Processes[context].Kill();
            }
        }

        /// <summary>
        /// Gets the name of the server.
        /// </summary>
        /// <value>
        /// The name of the server.
        /// </value>
        public override string ServerName => "Command-Line Terminal Server";
    }
}