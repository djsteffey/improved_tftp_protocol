using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace djs.network.tftp
{
    class Program
    {
        static void Main(string[] args)
        {
            CTFTPServer server = new CTFTPServer();
            server.run();
            
        }
    }
}
