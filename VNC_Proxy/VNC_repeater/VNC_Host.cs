using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace VNC_repeater
{
    public class VNC_Host
    {
        public enum Host_Type { VIEWER, SERVER };
        private readonly int BUFFER_LENGTH = 8192;
        public TcpClient[] Hosts;
        private byte[] Ping_Pong_Buffer_1;
        private byte[] Ping_Pong_Buffer_2;
        private DateTime Second_Counter;
        public int ID { get; set; }
        public DateTime Last_Time_Heard { get; set; }
        public bool Service_Running { get; set; }



        private Int64 _ThroughPut;//this is returned in bytes per second
        public Int64 ThroughPut
        {
            get
            {
                return _ThroughPut;
            }
        }
        public string ThroughPut_Pretty//this is returned in bytes per second
        {
            get
            {
                return SizeSuffix(_ThroughPut) + "s";
            }
        }

        private Int64 _Total_Data_Transfered;
        public Int64 Total_Data_Transfered
        {
            get
            {
                return _Total_Data_Transfered;
            }
        }
        public string Total_Data_Transfered_Pretty
        {
            get
            {
                return SizeSuffix(_Total_Data_Transfered) + "s";
            }
        }
        public VNC_Host()
        {
            Close();
        }

        public bool Paired
        {
            get
            {
                var server = Hosts[(int)Host_Type.SERVER];
                var viewer = Hosts[(int)Host_Type.VIEWER];
                if (server == null || viewer == null) return false;
                return server.Connected && viewer.Connected;
            }
        }
        public bool Pending_Pair
        {
            get
            {
                int c = (Hosts[(int)Host_Type.VIEWER] != null) ? 1 : 0;
                c += (Hosts[(int)Host_Type.SERVER] != null) ? 1 : 0;
                return c == 1;
            }
        }

        private void Close()
        {
            try
            {
                if (Hosts != null)
                {
                    var viewer = Hosts[(int)Host_Type.VIEWER];
                    if (viewer != null) viewer.Close();
                    var server = Hosts[(int)Host_Type.SERVER];
                    if (server != null) server.Close();
                }

            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            ID = -1;
            Last_Time_Heard = DateTime.Now;
            Hosts = new TcpClient[2];
            Service_Running = false;
            Ping_Pong_Buffer_1 = new byte[BUFFER_LENGTH];
            Ping_Pong_Buffer_2 = new byte[BUFFER_LENGTH];
            _ThroughPut = 0;
            Second_Counter = DateTime.Now;
            _Total_Data_Transfered = 0;
        }
        public bool Wait_For_Pairing()
        {
            Debug.WriteLine("Starting waiting on Pairing for ID " + ID);
            while (Service_Running)
            {
                if (Paired) break;//if the connections are paired, this loop can be broken out of
                else if (Pending_Pair) System.Threading.Thread.Sleep(100);
                else return false;//otherwise, the connection timed out.. 
            }
            if (!Service_Running)
            {
                Debug.WriteLine("Pairing stopped, likely due to inactivity");
                return false;
            }
            Debug.WriteLine("Pairing complete for ID " + ID);
            return true;
        }

        public void Service_Connections()
        {
            if (!Wait_For_Pairing())
            {// if this returns false, the pairing failed for some reason
                Close();
                return;
            }

            var server = Hosts[(int)Host_Type.SERVER];
            var viewer = Hosts[(int)Host_Type.VIEWER];
            if (server == null || viewer == null)
            {
                Close();
                return;
            }
            var tempid = ID;
            Debug.WriteLine("Service_Connections for ID " + tempid);
            while (server.Connected && viewer.Connected && Service_Running)
            {
                try
                {
                    var serverstream = server.GetStream();
                    var viewerstream = viewer.GetStream();
                    var sbuf_size = read(serverstream, Ping_Pong_Buffer_1);
                    var vbuf_size = read(viewerstream, Ping_Pong_Buffer_2);
                    write(viewerstream, Ping_Pong_Buffer_1, sbuf_size);
                    write(serverstream, Ping_Pong_Buffer_2, vbuf_size);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    break;//something happened break out and shut down the connection
                }

            }
            Close();
            Debug.WriteLine("Finished Service_Connections for ID " + tempid);
        }

        private int read(NetworkStream n, byte[] buffer)
        {
            if (n.DataAvailable)
            {
                if ((DateTime.Now - Second_Counter).TotalMilliseconds > 1000)
                {
                    Second_Counter = DateTime.Now;
                    _ThroughPut = 0;
                }
                Last_Time_Heard = DateTime.Now;
                var t = n.Read(buffer, 0, buffer.Length);
                _ThroughPut += t;
                _Total_Data_Transfered += t;
                return t;
            }
            return 0;
        }
        private void write(NetworkStream n, byte[] buffer, int num_bytes)
        {
            if (num_bytes > 0)
            {
                if ((DateTime.Now - Second_Counter).TotalMilliseconds > 1000)
                {
                    Second_Counter = DateTime.Now;
                    _ThroughPut = 0;
                }
                Last_Time_Heard = DateTime.Now;
                n.Write(buffer, 0, num_bytes);
                _ThroughPut += num_bytes;
                _Total_Data_Transfered += num_bytes;
            }
        }

        static readonly string[] SizeSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        static string SizeSuffix(Int64 value)
        {
            if (value <= 0) return "0 bytes";
            int mag = (int)Math.Log(value, 1024);
            decimal adjustedSize = (decimal)value / (1L << (mag * 10));

            return string.Format("{0:n1} {1}", adjustedSize, SizeSuffixes[mag]);
        }
    }
}
