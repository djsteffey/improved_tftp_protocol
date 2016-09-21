
using System;
using System.Net;
using System.Net.Sockets;

namespace djs.network.tftp
{
    public class CSocketUdp
    {
        // variables
        private Socket m_socket;
        private IPEndPoint m_remote_endpoint;

        // properties
        public int TimeoutInMilliseconds
        {
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

        // functions
        public CSocketUdp(int local_port = 0)
        {
            // create the socket
            this.m_socket = new Socket(SocketType.Dgram, ProtocolType.Udp);

            // default timeout of 5 seconds
            this.m_socket.ReceiveTimeout = 5000;
            this.m_socket.SendTimeout = 5000;

            // default remote of myself
            this.set_remote("localhost", local_port);

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
                    this.m_remote_endpoint = new IPEndPoint(address_list[0], remote_port);
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
            char[] c = System.Text.Encoding.ASCII.GetString(buffer).ToCharArray();


            if (this.m_socket.SendTo(buffer, length, SocketFlags.None, this.m_remote_endpoint) != length)
            {
                return false;
            }
            return true;
        }

        public int receive(byte[] buffer, ref IPEndPoint remote_endpoint)
        {
            try
            {
                EndPoint ep = (EndPoint)remote_endpoint;
                int bytes = this.m_socket.ReceiveFrom(buffer, ref ep);
                remote_endpoint = (IPEndPoint)ep;
                return bytes;
            }
            catch (SocketException)
            {
                // timeout or some other comms problem
                return 0;
            }
        }
    }
}
