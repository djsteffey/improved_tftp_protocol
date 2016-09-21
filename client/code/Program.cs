

using System;

namespace djs.network.tftp
{
    class Program
    {
        static void Main(string[] args)
        {
            CTFTPClient client = new CTFTPClient();
            client.get_file("testfile.dat", "152.23.44.12", 69);
            Console.ReadKey();
        }
    }
}
