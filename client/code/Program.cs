
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace djs.network.tftp
{
    class Program
    {
        static void Main(string[] args)
        {
            /*
            if (args.Length < 1)
            {
                Console.WriteLine("Must specify server name or ip");
                return;
            }*/
            string server_name = "127.0.0.1";// args[0];

            StreamWriter file = new StreamWriter("data.csv", true);
            
            // different variables for testing
            // latency will be externally controlled
            // this program will be ran for each latency that is changed externally

            List<float> list_dropchance = new List<float>() { 0.010f}; //0.0f, 0.005f, 0.01f };
            List<ushort> list_timeout = new List<ushort>() { 3 }; //1, 3 };
            List<uint> list_tsize = new List<uint>()
            {
                (1024 * 1024 * 8),              // one 8 MB "file" 
            };
            List<ushort> list_windowsize = new List<ushort>() { 2 }; //, 4, 8, 16, 32 };
            List<ushort> list_blksize = new List<ushort>() { 1468 }; //512, 1024 }; //, 1468, 2048, 4096 };




            // loop all possible variations
            foreach (float dropchance in list_dropchance)
            {
                foreach (ushort timeout in list_timeout)
                {
                    foreach (uint tsize in list_tsize)
                    {
                        foreach (ushort windowsize in list_windowsize)
                        {
                            foreach (ushort blksize in list_blksize)
                            {
				                for (int i = 0; i < 3; ++i)
				                {
	                                // run the test and spit the results into a file that i can import into a spreadsheet
	                                // for analyzing.   probably just some kind of csv file
	                                Console.WriteLine("************************************************************************");
					                Console.WriteLine("Itteration: " + (i + 1).ToString());
	                                string s = "input vars: dropchance=" + dropchance.ToString() +
	                                            "   timeout=" + timeout.ToString() + "sec" +
	                                            "   tsize=" + tsize.ToString() + "bytes" +
	                                            "   windowsize=" + windowsize.ToString() +
	                                            "   blksize=" + blksize.ToString() + "\n";
	                                Console.WriteLine(s);

                                    CTFTPNodeOutOfOrder client = new CTFTPNodeOutOfOrder();
	                                if (client.client_transfer_file(CTFTPNodeOutOfOrder.ETransferDirection.GET, server_name, 69,
	                                    "doesntmatter", blksize, timeout, tsize, windowsize, dropchance, true) == CTFTPNodeOutOfOrder.EStatus.OK)
	                                {
	                                    // transfer complete and successful
	                                    // record the stats
	                                    // right now just print
	                                    double latency = (client.LatencyAccumulator / client.LatencyMeasurements) * 1000;
	                                    s =         "output stats:  time=" + client.TotalTime.ToString() + " sec" +
	                                                "   duplicates=" + client.Duplicates.ToString() +
	                                                "   latency=" + latency.ToString() + "ms" +
	                                                "   goodput=" + (client.TotalSize / client.TotalTime / 1024.0f).ToString() + "KBps\n" +
	                                                "   senddrops=" + client.Socket.SimulatedDropSendCount.ToString() +
	                                                "   senddropbytes=" + client.Socket.SimulatedDropSendBytes.ToString() +
	                                                "   receivedrops=" + client.Socket.SimulatedDropReceiveCount.ToString() +
	                                                "   receivedropbytes=" + client.Socket.SimulatedDropReceiveBytes.ToString() +
	                                                "   sendpackets=" + client.Socket.SendPackets.ToString() +
	                                                "   sendbytes=" + client.Socket.SendBytes.ToString() +
	                                                "   receivepackets=" + client.Socket.ReceivePackets.ToString() +
	                                                "   receivebytes=" + client.Socket.ReceiveBytes.ToString();
	                                    Console.WriteLine(s);
	
	                                    // put the values in a csv
	
	                                    s = dropchance.ToString() + ", " + timeout.ToString() + ", " + tsize.ToString() + ", " +
	                                        windowsize.ToString() + ", " + blksize.ToString() + ", " + client.TotalTime.ToString() + ", " +
	                                        client.Duplicates.ToString() + ", " + latency.ToString() + ", " + client.Socket.SimulatedDropSendCount.ToString() + ", " +
	                                        client.Socket.SimulatedDropSendBytes.ToString() + ", " + client.Socket.SimulatedDropReceiveCount.ToString() + ", " +
	                                        client.Socket.SimulatedDropReceiveBytes.ToString() + ", " + client.Socket.SendPackets.ToString() + ", " +
	                                        client.Socket.SendBytes.ToString() + ", " + client.Socket.ReceivePackets.ToString() + ", " + client.Socket.ReceiveBytes.ToString();
	                                    file.WriteLine(s);
	                                    file.Flush();
	                                }
	                          
	
	                                Console.WriteLine("************************************************************************");
	
	                                // MUST sleep enough time to allow the server to timeout on the very last ACK
	                                // since retry is 3 then waiting for 4 retry timeouts is most safe
	                                Thread.Sleep(timeout * 4 * 1000);
				                }				
                            }
                        }
                    }
                }
            }
            
            Console.ReadKey();
        }
    }
}
