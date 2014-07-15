using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace VNC_repeater
{
    //this is a standard wrapper around the tcp read and write functions. Nothing special about this class
    public class Tcp_Socket: IVNC_Socket
    {
        private TcpClient _Host;
        public bool Connected { get { return _Host.Connected; } }
        public bool Available { get { return _Host.Available>0; } }
        public string ClientIpAddress { get { return ((IPEndPoint)_Host.Client.RemoteEndPoint).Address.ToString(); } }
        public int ClientPort { get { return ((IPEndPoint)_Host.Client.RemoteEndPoint).Port; } }
        public Tcp_Socket(TcpClient host)
        {
            _Host = host;
        }
        public void write(byte[] data, int num_of_bytes = -1)
        {
            Utility.write(_Host, data, num_of_bytes);
        }
        public int read(byte[] data)
        {
            return Utility.read(_Host, data);
        }

        public void Dispose()
        {
            _Host.Close();
            _Host = null;
        }
    }
}
