using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VNC_repeater
{
    class Program
    {
        static void Main(string[] args)
        {
            var server = new VNC_Proxy_Server();
            server.Start();
            Console.ReadLine();
        }
    }
}
