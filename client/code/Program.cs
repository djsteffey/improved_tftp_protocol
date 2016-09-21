

using System;

namespace djs.network.tftp
{
    class Program
    {
        static void Main(string[] args)
        {
            CTFTPClient client = new CTFTPClient();
            client.get_file("testfile.dat", "192.168.0.110", 69);
            Console.ReadKey();
        }
    }
}
