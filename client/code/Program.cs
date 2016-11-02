﻿
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
            if (args.Length < 1)
            {
                Console.WriteLine("Must specify server name or ip");
                return;
            }
            string server_name = args[0];

            StreamWriter file = new StreamWriter("data.csv", true);
            
            // different variables for testing
            // latency will be externally controlled
            // this program will be ran for each latency that is changed externally
            List<float> list_dropchance = new List<float>() { 0.0f, 0.0025f, 0.005f };
            List<ushort> list_timeout = new List<ushort>() { 1, 3 };
            List<uint> list_tsize = new List<uint>()
            {
//                (1024 * 256),
//                (1024 * 512),
//                (1024 * 1024 * 1),
//                (1024 * 1024 * 2),
                (1024 * 1024 * 4),              // one 4 MB "file" 
//                (1024 * 1024 * 8),
            };
            List<ushort> list_windowsize = new List<ushort>() { 1, 2, 4, 8, 16, 32 };
            List<ushort> list_blksize = new List<ushort>() { 512, 1468, 2048, 4096, 8192 };

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
                                // run the test and spit the results into a file that i can import into a spreadsheet
                                // for analyzing.   probably just some kind of csv file
                                Console.WriteLine("************************************************************************");
                                string s = "input vars: dropchance=" + dropchance.ToString() +
                                            "   timeout=" + timeout.ToString() + "sec" +
                                            "   tsize=" + tsize.ToString() + "bytes" +
                                            "   windowsize=" + windowsize.ToString() +
                                            "   blksize=" + blksize.ToString() + "\n";
                                Console.WriteLine(s);

                                CTFTPNode client = new CTFTPNode();
                                if (client.client_transfer_file(CTFTPNode.ETransferDirection.GET, server_name, 69,
                                    "doesntmatter", blksize, timeout, tsize, windowsize, dropchance, false) == CTFTPNode.EStatus.OK)
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
            
            Console.ReadKey();
        }
    }
}
