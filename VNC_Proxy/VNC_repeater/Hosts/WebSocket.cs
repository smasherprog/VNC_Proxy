using Fleck;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace VNC_repeater
{
    public class WebSocket : IVNC_Socket
    {

        private IWebSocketConnection _Host;
        private bool _Connected;
        public bool Connected { get { return _Connected; } }
        public bool Available { get { return _Buffer.Count > 0 || _WorkingBuffer != null; } }
        public string ClientIpAddress { get { return _Host.ConnectionInfo.ClientIpAddress; } }
        public int ClientPort { get { return _Host.ConnectionInfo.ClientPort; } }
        private ConcurrentQueue<byte[]> _Buffer;
        private byte[] _WorkingBuffer = null;

        public WebSocket(IWebSocketConnection host)
        {
            _Host = host;
            _Connected = true;
            host.OnClose = () => { _Connected = false; };
            host.OnBinary = (a) => { _Buffer.Enqueue(a); };
            _Buffer = new ConcurrentQueue<byte[]>();
        }
        public void write(byte[] data, int num_of_bytes = -1)
        {
            if (num_of_bytes == 0) return;
            int bytestosend = 0;
            byte[] tempbuff = null;

            if (num_of_bytes == -1)  bytestosend = data.Length;
            else bytestosend = num_of_bytes;
     
            tempbuff = new byte[bytestosend];
            Array.Copy(data, tempbuff, bytestosend);
            _Host.Send(tempbuff);
        }
        public int read(byte[] data)
        {
            if (_WorkingBuffer == null) _Buffer.TryDequeue(out _WorkingBuffer);
            if (_WorkingBuffer != null)
            {
                var d = _WorkingBuffer.Length;
                Array.Copy(_WorkingBuffer, data, d);
                _WorkingBuffer = null;
                return d;
            }
            return 0;
        }
        public void Dispose()
        {
            _Host.Close();
            _Host = null;
        }

    }
}
