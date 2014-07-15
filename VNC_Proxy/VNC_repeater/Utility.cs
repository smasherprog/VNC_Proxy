using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VNC_repeater
{
    public static class Utility
    {
        public enum Host_Type { VIEWER, SERVER };

        //these two functions, write and read are called for ALL sends and receives. If you want to see what is actually being sent back and forth for all communication, this would be where you would
        //hook that in
        public static void write(TcpClient host, byte[] data, int num_of_bytes = -1)
        {
            var stream = host.GetStream();
            //if the user specifies how many bytes to write, use that, otherwise assume the entire buffer should be sent
            if (num_of_bytes == -1) stream.Write(data, 0, data.Length);
            else stream.Write(data, 0, num_of_bytes);
            Debug.WriteLine("SEND\n" + Encoding.UTF8.GetString(data));
        }
        public static int read(TcpClient host, byte[] data)
        {
            var stream = host.GetStream();
            if (stream.DataAvailable)
            {
                var d = stream.Read(data, 0, data.Length);
                Debug.WriteLine("READ\n" + Encoding.UTF8.GetString(data));
                return d;
            }
            return 0;
        }
        public static int ParseID(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return -1;
            int t = -1;
            if (Int32.TryParse(data, out t)) return t;
            var endofdata = data.IndexOf('\0');
            if (endofdata < 0) return -1;
            var beginofdata = data.IndexOf(':');
            if (beginofdata < 0) return -1;
            
            if (Int32.TryParse(data.Substring(beginofdata + 1, endofdata - beginofdata), out t)) return t;
            return -1;
        }

        static readonly string[] SizeSuffixes = { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        public static string SizeSuffix(Int64 value)
        {
            if (value <= 0) return "0 bytes";
            int mag = (int)Math.Log(value, 1024);
            decimal adjustedSize = (decimal)value / (1L << (mag * 10));

            return string.Format("{0:n1} {1}", adjustedSize, SizeSuffixes[mag]);
        }
  

    }
}
