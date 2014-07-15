using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;

namespace Fleck
{
    public class WebSocketServer : IWebSocketServer
    {
        private readonly string _scheme;
        private Action<IWebSocketConnection> _config;

        public WebSocketServer(string location)
            : this(8181, location)
        {
        }

        public WebSocketServer(int port, string location)
        {
            var uri = new Uri(location);
            Port = uri.Port > 0 ? uri.Port : port;
            Location = location;
            _scheme = uri.Scheme;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            ListenerSocket = new SocketWrapper(socket);
            SupportedSubProtocols = new string[0];
        }

        public ISocket ListenerSocket { get; set; }
        public string Location { get; private set; }
        public int Port { get; private set; }
        public X509Certificate2 Certificate { get; set; }
        public IEnumerable<string> SupportedSubProtocols { get; set; }

        public bool IsSecure
        {
            get { return Certificate != null; }
        }

        public void Dispose()
        {
            ListenerSocket.Dispose();
        }

        public void Start(Action<IWebSocketConnection> config)
        {
            var ipLocal = new IPEndPoint(IPAddress.Any, Port);
            ListenerSocket.Bind(ipLocal);
            ListenerSocket.Listen(100);
            FleckLog.Info("Server started at " + Location);
            if (_scheme == "wss")
            {
                if (Certificate == null)
                {
                    FleckLog.Warn("No certificate loaded, only ws:// (and not wss://) connections will be accepted");
                }
            }
            ListenForClients();
            _config = config;
        }

        private void ListenForClients()
        {
            ListenerSocket.Accept(OnClientConnect, e =>
            {
                FleckLog.Error("Listener socket is closed", e);
                FleckLog.Info("Listener socket restarting");
                try
                {
                    ListenerSocket.Dispose();
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
                    ListenerSocket = new SocketWrapper(socket);
                    Start(_config);
                    FleckLog.Info("Listener socket restarted");
                }
                catch (Exception ex)
                {
                    FleckLog.Error("Listener could not be restarted", ex);
                }
            });
        }

        private void OnClientConnect(ISocket clientSocket)
        {
            FleckLog.Debug(String.Format("Client connected from {0}:{1}", clientSocket.RemoteIpAddress, clientSocket.RemotePort.ToString()));
            ListenForClients();

            WebSocketConnection connection = null;

            connection = new WebSocketConnection(
                clientSocket,
                _config,
                bytes => RequestParser.Parse(bytes, _scheme),
                r => HandlerFactory.BuildHandler(r,
                                                 s => connection.OnMessage(s),
                                                 connection.Close,
                                                 b => connection.OnBinary(b),
                                                 b => connection.OnPing(b),
                                                 b => connection.OnPong(b)),
                s => SubProtocolNegotiator.Negotiate(SupportedSubProtocols, s));

            // Determine whether this is a ws or wss connection
            try
            {
                // Wait up to 5 seconds for the first handshake byte
                // (Only peek, so it's still in the buffer for the actual handshake handler)
                byte[] buffer = new byte[1];
                clientSocket.Socket.ReceiveTimeout = 5000;
                int BytesRead = clientSocket.Socket.Receive(buffer, 1, SocketFlags.Peek);
                clientSocket.Socket.ReceiveTimeout = 0;
                if (BytesRead == 1)
                {
                    if ((buffer[0] == 0x16) || (buffer[0] == 0x80))
                    {
                        // wss connection, ensure we have a certificate
                        if (IsSecure)
                        {
                            FleckLog.Info("Accepting wss:// Connection");
                            clientSocket
                                .Authenticate(Certificate,
                                              connection.StartReceiving,
                                              e =>
                                              {
                                                  FleckLog.Warn("Failed to Authenticate", e);
                                                  connection.Close();
                                              });
                        }
                        else
                        {
                            FleckLog.Warn("Rejecting wss:// connection (no certificate)");
                            connection.Close();
                        }
                    }
                    else
                    {
                        // ws connection
                        FleckLog.Info("Accepting ws:// Connection");
                        connection.StartReceiving();
                    }
                }
                else
                {
                    connection.Close();
                }
            }
            catch (Exception ex)
            {
                FleckLog.Error("Unable to read handshake byte from client", ex);
                connection.Close();
            }
        }
    }
}
