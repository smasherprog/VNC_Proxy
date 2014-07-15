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
    //this class holds a pairing. i.e. a viewer and server pairing that communicate with each other
    public class VNC_Pair
    {
        
        private readonly int BUFFER_LENGTH = 8192;
        private IVNC_Socket[] Hosts;
        private byte[] Ping_Pong_Buffer_1;
        private byte[] Ping_Pong_Buffer_2;
        private DateTime Second_Counter;
        public int ID { get; set; }
        public DateTime Last_Time_Heard { get; set; }
        public bool Service_Running { get; set; }

        private Int64 _ThroughPut;//this is returned in bytes per second
        public Int64 ThroughPut { get { return _ThroughPut; } }
        public string ThroughPut_Pretty { get { return Utility.SizeSuffix(_ThroughPut) + "s"; } }//this is returned in bytes per second

        private Int64 _Total_Data_Transfered;
        public Int64 Total_Data_Transfered { get { return _Total_Data_Transfered; } }
        public string Total_Data_Transfered_Pretty { get { return Utility.SizeSuffix(_Total_Data_Transfered) + "s"; } }
        public VNC_Pair()
        {
            Hosts = new IVNC_Socket[2];
            Close();
        }
        //returns how many hosts are connected, can return 0, 1, or 2
        public int Host_Count()
        {
            return ((Hosts[(int)VNC_repeater.Utility.Host_Type.VIEWER] != null) ? 1 : 0) + ((Hosts[(int)VNC_repeater.Utility.Host_Type.SERVER] != null) ? 1 : 0);
        }
        private object _HostGuard = new object();

        public bool Add(IVNC_Socket h, VNC_repeater.Utility.Host_Type t)
        {
            lock (_HostGuard)
            {
                if (Hosts[(int)t] == null)
                {
                    Hosts[(int)t] = h;
                    Last_Time_Heard = DateTime.Now;
                    return true;
                }
            }
            return false;
        }
        public bool Paired
        {
            get
            {
                var server = Hosts[(int)VNC_repeater.Utility.Host_Type.SERVER];
                var viewer = Hosts[(int)VNC_repeater.Utility.Host_Type.VIEWER];
                if (server == null || viewer == null) return false;
                return server.Connected && viewer.Connected;
            }
        }
        public bool Pending_Pair(){ return Host_Count() == 1; }
        private void CloseHost(IVNC_Socket h){
            if (h != null) h.Dispose();
        }
        private void Close()
        {
            try
            {
                CloseHost(Hosts[(int)VNC_repeater.Utility.Host_Type.VIEWER]);
                CloseHost(Hosts[(int)VNC_repeater.Utility.Host_Type.SERVER]);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.Message);
            }
            ID = -1;
            Last_Time_Heard = DateTime.Now;
            Hosts = new IVNC_Socket[2];
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
                else if (Pending_Pair()) System.Threading.Thread.Sleep(100);//keep waiting for a connection pairing
                else return false;//otherwise, the connection timed out.. 
            }
            if (!Service_Running)
            {
                Debug.WriteLine("Pairing stopped, likely due to inactivity");// if Service_Running is false it means the vnc_procy class set it to false in its idle loop check
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
            // both viewer and client should be connected now.. start proxy service
            var server = Hosts[(int)VNC_repeater.Utility.Host_Type.SERVER];
            var viewer = Hosts[(int)VNC_repeater.Utility.Host_Type.VIEWER];

            var tempid = ID;
            Debug.WriteLine("Service_Connections for ID " + tempid);
            while (server.Connected && viewer.Connected && Service_Running)
            {
                try
                {
                    var sbuf_size = read(server, Ping_Pong_Buffer_1);
                    var vbuf_size = read(viewer, Ping_Pong_Buffer_2);
                    write(viewer, Ping_Pong_Buffer_1, sbuf_size);
                    write(server, Ping_Pong_Buffer_2, vbuf_size);
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

        private int read(IVNC_Socket n, byte[] buffer)
        {
            if (n.Available)
            {
                if ((DateTime.Now - Second_Counter).TotalMilliseconds > 1000)
                {
                    Second_Counter = DateTime.Now;
                    _ThroughPut = 0;
                }
                Last_Time_Heard = DateTime.Now;
                var t = n.read(buffer);
                _ThroughPut += t;
                _Total_Data_Transfered += t;
                return t;
            }
            return 0;
        }
        private void write(IVNC_Socket n, byte[] buffer, int num_bytes)
        {
            if (num_bytes > 0)
            {
                if ((DateTime.Now - Second_Counter).TotalMilliseconds > 1000)
                {
                    Second_Counter = DateTime.Now;
                    _ThroughPut = 0;
                }
                Last_Time_Heard = DateTime.Now;
                n.write(buffer, num_bytes);
                _ThroughPut += num_bytes;
                _Total_Data_Transfered += num_bytes;
            }
        }


    }
}
