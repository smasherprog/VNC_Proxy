using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace VNC_repeater
{

    public class VNC_Proxy_Server
    {
        private readonly int MAX_CONNECTIONS = 20;
        private readonly int IDLE_DISCONNECT_TIME = 20; //in seconds
        private readonly int MAX_HOST_NAME_LEN = 250;// this is the size of the header.. dont overread or underread
        private VNC_Host[] VNC_Proxy_Connections;
        private ConcurrentQueue<int> Unused_IDs;

        private bool KeepRunning;

        private int Viewer_Listen_Port;
        private int Server_Listen_Port;
        private List<Task> Tasks;

        public VNC_Proxy_Server(int viewerlistenport = 5901, int serverlistenport = 5500)
        {
            Server_Listen_Port = serverlistenport;
            Viewer_Listen_Port = viewerlistenport;
            VNC_Proxy_Connections = new VNC_Host[MAX_CONNECTIONS];
            Tasks = new List<Task>();
            Unused_IDs = new ConcurrentQueue<int>();
            for (int i = 0; i < MAX_CONNECTIONS; i++) Unused_IDs.Enqueue(i);
        }
        //start a new task which will just listen for new connections from either servers or clients
        public void Start()
        {
            KeepRunning = true;
            Tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Listen_For_Connections(new IPEndPoint(IPAddress.Any, Viewer_Listen_Port), Process_Connection_Request_From_Viewers);
            }));
            //this task will listen for connection requests from servers
            Tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Listen_For_Connections(new IPEndPoint(IPAddress.Any, Server_Listen_Port), Process_Connection_Request_From_Servers);
            }));
            //this task will drop any connections which are idle or inactive
            Tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                Check_For_Timeout(VNC_Proxy_Connections);
            }));

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
            foreach (var item in Tasks)
            {
                item.Wait(miliseconds / 3);
            }
            Tasks = new List<Task>();
        }

        private void Check_For_Timeout(VNC_Host[] arr)
        {
            while (KeepRunning)
            {
                for (int i = 0; i < MAX_CONNECTIONS; i++)
                {
                    if (arr[i] == null) continue;
                    var tmp = arr[i];
                    if ((DateTime.Now - tmp.Last_Time_Heard).TotalSeconds > IDLE_DISCONNECT_TIME)
                    {//if it has been more than 30 seconds disconnect users
                        arr[i] = null;
                        Debug.WriteLine("Disconnecting tcp connection id " + tmp.ID + " due to lack of connectivity ");
                        tmp.Service_Running = false;//this will shut down the underlying thread and connections. This is not a synchronous operation
                        Unused_IDs.Enqueue(i);
                    }
                }
                System.Threading.Thread.Sleep(5000);// this doesnt need to be called that often
            }
        }
        private void Listen_For_Connections(IPEndPoint endpoint, Func<TcpClient, bool> process_func)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(endpoint);
                listener.Start();
                Debug.WriteLine("Started Listening on " + endpoint.ToString());
                while (KeepRunning)
                {
                    try
                    {
                        var possibleclient = listener.AcceptTcpClient();
                        possibleclient.NoDelay = true;
                        Debug.WriteLine("Connection attempt from " + possibleclient.Client.RemoteEndPoint.ToString() + " to " + possibleclient.Client.LocalEndPoint.ToString());
                        if (!process_func(possibleclient))
                        {
                            Debug.WriteLine("Disconnecting " + possibleclient.Client.RemoteEndPoint.ToString());
                            possibleclient.Close();
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            if (listener != null) listener.Stop();
        }

        private bool Process_Connection_Request_From_Servers(TcpClient client)
        {

            var stream = client.GetStream();
            var bytes = new byte[MAX_HOST_NAME_LEN];
            System.Threading.Thread.Sleep(500);// small pause

            var i = read(stream, bytes);
            if (i > 0)
            {
                var id = ParseID(System.Text.Encoding.ASCII.GetString(bytes, 0, i));
                if (id != -1) return Add(client, id, VNC_Host.Host_Type.SERVER);

            }

            return false;
        }
        private bool Process_Connection_Request_From_Viewers(TcpClient client)
        {

            var stream = client.GetStream();
            write(stream, System.Text.Encoding.ASCII.GetBytes("RFB 000.000\n"));
            var bytes = new byte[MAX_HOST_NAME_LEN];
            System.Threading.Thread.Sleep(500);// small pause

            var i = read(stream, bytes);
            if (i > 0)
            {
                var id = ParseID(System.Text.Encoding.ASCII.GetString(bytes, 0, i));
                if (id != -1) return Add(client, id, VNC_Host.Host_Type.VIEWER);
            }

            return false;
        }
        private bool Add(TcpClient h, int id, VNC_repeater.VNC_Host.Host_Type t)
        {
            Debug.WriteLine("Add " + Enum.GetName(t.GetType(), t) + " for id " + id);
            //first check to see if any viewers are already connected with the same id

            for (int i = 0; i < VNC_Proxy_Connections.Length; i++)
            {
                if (VNC_Proxy_Connections[i] == null) continue;
                var pair = VNC_Proxy_Connections[i];
                if (pair.ID == id)
                {
                    if (pair.Hosts[(int)t] != null)
                    {
                        Debug.WriteLine(id + " is already in used!");
                        return false;
                    }
                    else
                    {
                        Debug.WriteLine("Pairing found, starting to service the pair: " + id);
                        pair.Hosts[(int)t] = h;
                        pair.Last_Time_Heard = DateTime.Now;
                        return true;
                        //if an id is found, that means there is already a host waiting for a pairing. 
                        //setting the other Host will cause the pair to be made
                    }
                }
            }
            var unusedid = -1;
            Debug.WriteLine("First connect request with the id of " + id);
            if (Unused_IDs.TryDequeue(out unusedid))
            {
                var tmp = new VNC_Host();
                tmp.Service_Running = true;
                tmp.ID = id;
                tmp.Hosts[(int)t] = h;
                VNC_Proxy_Connections[unusedid] = tmp;
                System.Threading.Tasks.Task.Factory.StartNew(() =>
                {
                    tmp.Service_Connections();
                });
                return true;
            }
            Debug.WriteLine("No more slots available to service the incomming connection request.");
            return false;
        }



        private void write(NetworkStream stream, byte[] data)
        {
            stream.Write(data, 0, data.Length);
        }
        private int read(NetworkStream stream, byte[] data)
        {
            return stream.Read(data, 0, data.Length);
        }

        private int ParseID(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return -1;
            var endofdata = data.IndexOf('\0');
            if (endofdata < 0) return -1;
            var beginofdata = data.IndexOf(':');
            if (beginofdata < 0) return -1;
            int t = -1;
            if (Int32.TryParse(data.Substring(beginofdata + 1, endofdata - beginofdata), out t)) return t;
            return -1;
        }
    }
}
