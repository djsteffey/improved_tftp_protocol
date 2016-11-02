
using System;
using System.Net;
using System.Net.Sockets;

namespace djs.network
{
    public class CSocketUdp
    {
        // variables
        private Socket m_socket;
        private IPEndPoint m_remote_endpoint;
        private float m_simulated_drop_chance_send;
        private float m_simulated_drop_chance_receive;
        private Random m_rand;
        private long m_send_packets;
        private long m_send_bytes;
        private long m_receive_packets;
        private long m_receive_bytes;
        private long m_simulated_drops_send;
        private long m_simulated_drops_receive;
        private long m_simulated_drops_send_bytes;
        private long m_simulated_drops_receive_bytes;

        // properties
        public int TimeoutInMilliseconds
        {
            get { return this.m_socket.ReceiveTimeout; }
            set
            {
                this.m_socket.ReceiveTimeout = value;
                this.m_socket.SendTimeout = value;
            }
            
        }
        public IPEndPoint RemoteEndpoint
        {
            get { return this.m_remote_endpoint; }
        }
        public float SimulatedDropChanceSend
        {
            get { return this.m_simulated_drop_chance_send; }
            set { this.m_simulated_drop_chance_send = Math.Max(0.0f, Math.Min(value, 1.0f)); }
        }
        public float SimulatedDropChanceReceive
        {
            get { return this.m_simulated_drop_chance_receive; }
            set { this.m_simulated_drop_chance_receive = Math.Max(0.0f, Math.Min(value, 1.0f)); }
        }
        public long SimulatedDropSendCount
        {
            get { return this.m_simulated_drops_send; }
        }
        public long SimulatedDropReceiveCount
        {
            get { return this.m_simulated_drops_receive; }
        }
        public long SimulatedDropSendBytes
        {
            get { return this.m_simulated_drops_send_bytes; }
        }
        public long SimulatedDropReceiveBytes
        {
            get { return this.m_simulated_drops_receive_bytes; }
        }
        public long SendBytes
        {
            get { return this.m_send_bytes; }
        }
        public long SendPackets
        {
            get { return this.m_send_packets; }
        }
        public long ReceiveBytes
        {
            get { return this.m_receive_bytes; }
        }
        public long ReceivePackets
        {
            get { return this.m_receive_packets; }
        }

        // functions
        public CSocketUdp(int local_port = 0)
        {
            // create the socket
            this.m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            if (local_port != 0)
            {
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, local_port);
                this.m_socket.Bind(ep);
            }

            // default timeout of 5 seconds
            this.m_socket.ReceiveTimeout = 5000;
            this.m_socket.SendTimeout = 5000;

            // default remote of myself
            this.set_remote("localhost", local_port);

            // no simulated drop chance
            this.m_simulated_drop_chance_send = 0.0f;
            this.m_simulated_drop_chance_receive = 0.0f;
            this.m_rand = new Random();

            // stats
            this.m_send_packets = 0;
            this.m_receive_packets = 0;
            this.m_simulated_drops_send = 0;
            this.m_simulated_drops_receive = 0;
            this.m_send_bytes = 0;
            this.m_receive_bytes = 0;
            this.m_simulated_drops_send_bytes = 0;
            this.m_simulated_drops_receive_bytes = 0;
        }

        public void close()
        {
            // close the socket
            this.m_socket.Close();
        }

        public void set_remote(string remote_name, int remote_port)
        {
            // turn the remote_name and port into the remote_endpoint
            IPAddress[] address_list = Dns.GetHostAddresses(remote_name);
            // just use the first address in the list
            foreach (IPAddress address in address_list)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    this.m_remote_endpoint = new IPEndPoint(address, remote_port);
                    return;
                }
            }
        }

        public void set_remote(IPEndPoint remote_endpoint)
        {
            this.m_remote_endpoint = remote_endpoint;
        }

        public bool send(byte[] buffer, int length)
        {
            // see if we should drop it
            if (this.m_simulated_drop_chance_send > 0.0f)
            {
                if (this.m_rand.NextDouble() <= this.m_simulated_drop_chance_send)
                {
                    // drop it like its hot
                    // but return as though we were successful
                    this.m_simulated_drops_send += 1;
                    this.m_simulated_drops_send_bytes += length;
                    return true;
                }
            }

            // not dropping so do it
            char[] c = System.Text.Encoding.ASCII.GetString(buffer).ToCharArray();

            if (this.m_socket.SendTo(buffer, length, SocketFlags.None, this.m_remote_endpoint) != length)
            {
                return false;
            }
            this.m_send_packets += 1;
            this.m_send_bytes += length;
            return true;
        }

        public bool send(byte[] buffer, int length, IPEndPoint remote_endpoint)
        {
            // see if we should drop it
            if (this.m_simulated_drop_chance_send > 0.0f)
            {
                if (this.m_rand.NextDouble() <= this.m_simulated_drop_chance_send)
                {
                    // drop it like its hot
                    // but return as though we were successful
                    this.m_simulated_drops_send += 1;
                    this.m_simulated_drops_send_bytes += length;
                    return true;
                }
            }

            // not dropping so do it
            char[] c = System.Text.Encoding.ASCII.GetString(buffer).ToCharArray();


            if (this.m_socket.SendTo(buffer, length, SocketFlags.None, remote_endpoint) != length)
            {
                return false;
            }
            this.m_send_packets += 1;
            this.m_send_bytes += length;
            return true;
        }

        public int receive(byte[] buffer, out IPEndPoint remote_endpoint)
        {
            try
            {
                // create the endpoint to capture the remote address
                EndPoint ep = (EndPoint)(new IPEndPoint(IPAddress.Any, 0));
                // receive the data
                int bytes = this.m_socket.ReceiveFrom(buffer, ref ep);
                // return the remote_endpoint back to the caller
                remote_endpoint = (IPEndPoint)ep;

                // we got a packet now determine if we should drop it
                if (this.m_simulated_drop_chance_receive > 0.0f)
                {
                    if (this.m_rand.NextDouble() <= this.m_simulated_drop_chance_receive)
                    {
                        // drop it like its hot
                        // but return as though we didnt get anything
                        remote_endpoint = null;
                        this.m_simulated_drops_receive += 1;
                        this.m_simulated_drops_receive_bytes += bytes;
                        return 0;
                    }
                }

                this.m_receive_packets += 1;
                this.m_receive_bytes += bytes;
                return bytes;
            }
            catch (SocketException)
            {
                // timeout or some other comms problem
                remote_endpoint = null;
                return 0;
            }
        }

        public static IPEndPoint convert_to_ipendpoint(string name, int port)
        {
            // turn the remote_name and port into the remote_endpoint
            IPAddress[] address_list = Dns.GetHostAddresses(name);
            // just use the first address in the list
            foreach (IPAddress address in address_list)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return new IPEndPoint(address, port);
                }
            }
            return null;
        }
    }
}
