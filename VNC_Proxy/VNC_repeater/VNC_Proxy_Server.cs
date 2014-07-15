using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;
using Fleck;
using System.Threading;

namespace VNC_repeater
{

    public class VNC_Proxy_Server
    {
   
        private readonly int MAX_CONNECTIONS = 20;
        private readonly int IDLE_DISCONNECT_TIME = 20; //in seconds

        private VNC_Pair[] VNC_Proxy_Connections;
        private ConcurrentQueue<int> Unused_IDs;
        private ConcurrentQueue<IVNC_Socket> Pending_WebSockets;

        private bool KeepRunning;

        private int Viewer_Listen_Port;//standard tcp connections from viewers
        private int Viewer_Listen_Port_Browsers;//adds web sockets support
        private int Server_Listen_Port;
        private List<Task> Tasks;
        private WebSocketServer server;


        public VNC_Proxy_Server(int viewerlistenport = 5901, int browserviewerlistenport = 5902, int serverlistenport = 5500)
        {
            Server_Listen_Port = serverlistenport;
            Viewer_Listen_Port = viewerlistenport;
            Viewer_Listen_Port_Browsers = browserviewerlistenport;
            VNC_Proxy_Connections = new VNC_Pair[MAX_CONNECTIONS];
            Tasks = new List<Task>();
            Unused_IDs = new ConcurrentQueue<int>();
            Pending_WebSockets = new ConcurrentQueue<IVNC_Socket>();
            for (int i = 0; i < MAX_CONNECTIONS; i++) Unused_IDs.Enqueue(i);

        }
        //Start() creates three threads: One to listen for Viewer connections; One to listen for server connections and one to disconnect idle connections
        public void Start()
        {
            KeepRunning = true;
            Tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Listen_For_Connections(new IPEndPoint(IPAddress.Any, Viewer_Listen_Port), On_Viewer_Connect);
            }));
            //this task will listen for connection requests from servers
            Tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Listen_For_Connections(new IPEndPoint(IPAddress.Any, Server_Listen_Port), On_Server_Connect);
            }));
            //this task will process connection requests from websocket viewers that come fromthe Fleck Library
            Tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Process_PendingWebSockets();
            }));
            //this task will drop any connections which are idle or inactive
            Tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Check_For_Timeout();
            }));
            FleckLog.Level = LogLevel.Debug;

            server = new WebSocketServer("ws://localhost:" + Viewer_Listen_Port_Browsers.ToString());
            server.SupportedSubProtocols = new[] { "binary" };
            
            server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    Pending_WebSockets.Enqueue(new WebSocket(socket));
                };
            });
          
        }

        public void Stop()
        {
            KeepRunning = false;
        }
        //try to shut down by waiting on the tasks to complete instead of a hard stop. This effectivly calls join on each task
        public void Stop(int miliseconds)
        {
            KeepRunning = false;
            if (miliseconds < Tasks.Count) return;
            foreach (var item in Tasks) item.Wait(miliseconds / 3);
            Tasks = new List<Task>();
        }

        private void Check_For_Timeout()
        {
            while (KeepRunning)
            {
                for (int i = 0; i < MAX_CONNECTIONS; i++)
                {
                    if (VNC_Proxy_Connections[i] == null) continue;
                    var tmp = VNC_Proxy_Connections[i];
                    if ((DateTime.Now - tmp.Last_Time_Heard).TotalSeconds > IDLE_DISCONNECT_TIME)
                    {//if it has been more than 30 seconds disconnect users
                        VNC_Proxy_Connections[i] = null;
                        Debug.WriteLine("Disconnecting tcp connection id " + tmp.ID + " due to lack of connectivity ");
                        tmp.Service_Running = false;//this will shut down the underlying thread and connections. This is not a synchronous operation, It will stop soon
                        Unused_IDs.Enqueue(i);
                    }
                }
                System.Threading.Thread.Sleep(5000);// this doesnt need to be called that often
            }
        }
        private void Process_PendingWebSockets()
        {
            while (KeepRunning)
            {
                try
                {
                    IVNC_Socket h = null;
                    if (Pending_WebSockets.TryDequeue(out h))
                    {
                        Console.WriteLine("Processing websocket Connect Request");
                        Process_Connections(h, On_Viewer_Connect);
                    }
                    System.Threading.Thread.Sleep(20);//sleep for 20 milliseconds.. no need to be greedy 
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }
        }
        private void Process_Connections(IVNC_Socket h, Func<IVNC_Socket, bool> process_func)
        {
            Debug.WriteLine("Connection attempt from " + h.ClientIpAddress + " to " + h.ClientPort);
            if (!process_func(h))
            {
                Debug.WriteLine("Disconnecting " + h.ClientIpAddress);
                h.Dispose();
            }
        }
        private void Listen_For_Connections(IPEndPoint endpoint, Func<IVNC_Socket, bool> process_func)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(endpoint);
                listener.Start();
                Debug.WriteLine("Started Listening on " + endpoint.ToString());
                while (KeepRunning)
                {
                    TcpClient possibleclient = null;
                    try
                    {
                        listener.AcceptTcpClientAsync();
                        possibleclient = listener.AcceptTcpClient();
                        possibleclient.NoDelay = true;
                        Process_Connections(new Tcp_Socket(possibleclient), process_func);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.Message);
                        Debug.WriteLine("ERROR: Disconnecting " + possibleclient.Client.RemoteEndPoint.ToString());
                        possibleclient.Close();
                        possibleclient = null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            if (listener != null)
            {
                listener.Stop();
                listener = null;
            }
        }

        private bool On_Server_Connect(IVNC_Socket h)
        {
            var id = GetID(h);
            if (id > -1) return Add(h, id, VNC_repeater.Utility.Host_Type.SERVER);
            return false;
        }
        private bool On_Viewer_Connect(IVNC_Socket h)
        {
            h.write(System.Text.Encoding.UTF8.GetBytes("RFB 000.000\n"));
            var id = GetID(h);
            if (id > -1) return Add(h, id, VNC_repeater.Utility.Host_Type.VIEWER);
            return false;
        }
        //parses the id portion of what the host sends back and extracts it
        private int GetID(IVNC_Socket h)
        {
            int maxiterations = 0;
            while (!h.Available && maxiterations++ < 5) { System.Threading.Thread.Sleep(100); }// wait for response
            var bytes = new byte[250];
            var i = h.read(bytes);
            if (i > 0) return Utility.ParseID(System.Text.Encoding.UTF8.GetString(bytes, 0, i));
            return -1;//return an invalid id
        }
        private bool Add(IVNC_Socket h, int id, VNC_repeater.Utility.Host_Type t)
        {
            Debug.WriteLine("Add " + Enum.GetName(t.GetType(), t) + " for id " + id);
            //first check to see if any hosts already connected with the same id and host type

            for (int i = 0; i < VNC_Proxy_Connections.Length; i++)
            {
                if (VNC_Proxy_Connections[i] == null) continue;
                var pair = VNC_Proxy_Connections[i];
                if (pair.ID == id)
                {
                    var ret = pair.Add(h, t);//this is atomic internally to guard against multiple threads working on the same object
                    if (!ret) Debug.WriteLine(id + " is already in used!");
                    else Debug.WriteLine("Pairing found, starting to service the pair: " + id);
                    return ret;
                }
            }
            var unusedid = -1;
            Debug.WriteLine("First connect request with the id of " + id);
            if (Unused_IDs.TryDequeue(out unusedid))
            {
                var tmp = new VNC_Pair();
                tmp.Service_Running = true;
                tmp.ID = id;
                tmp.Add(h, t);//this should always succeed
                VNC_Proxy_Connections[unusedid] = tmp;
                System.Threading.Tasks.Task.Factory.StartNew(() =>
                {//start a thread to service this connection and wait for a pairing
                    tmp.Service_Connections();
                });
                return true;
            }
            Debug.WriteLine("No more slots available to service the incomming connection request.");
            return false;
        }

    }
}
