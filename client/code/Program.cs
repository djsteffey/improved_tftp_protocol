

using System;

namespace djs.network.tftp
{
    class Program
    {
        static void Main(string[] args)
        {
            CTFTPClient client = new CTFTPClient();
            client.send_file("testfile.dat", "127.0.0.1", 69);
            Console.ReadKey();
        }
    }
}
