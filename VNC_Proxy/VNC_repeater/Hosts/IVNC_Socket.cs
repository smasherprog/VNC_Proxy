using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace VNC_repeater
{
    public interface IVNC_Socket : IDisposable
    {
        //abstract write because websockets use a different method for writing data to the tcp socket. Standard Tcp_Socket is just a wrapper around the tcpclient
        void write(byte[] data, int num_of_bytes = -1);
        //abstract read because websockets use a different method for reading data to the tcp socket. Standard Tcp_Socket is just a wrapper around the tcpclient
        int read(byte[] data);
        bool Connected { get; }
        bool Available { get; }
        string ClientIpAddress { get; }
        int ClientPort { get; }

        //this should return a number>=0 for a valid handshake and ID parsing. If its < 0, there was an error somewhere, disconnect
    }
}
