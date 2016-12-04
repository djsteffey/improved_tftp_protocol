

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace djs.network.tftp
{
    public class CTFTPServer
    {
        // variables

        // properties
        private CSocketUdp m_listen_socket;

        // functions
        public CTFTPServer()
        {
            this.m_listen_socket = new CSocketUdp(69);
            this.m_listen_socket.TimeoutInMilliseconds = 0;
        }

        public void run()
        {
            bool running = true;

            // buffer for receipt of messages
            byte[] receive_buffer = new byte[65536];
            int receive_bytes = 0;

            while (running)
            {
                // wait for a message
                IPEndPoint remote_endpoint = null;

                // try to receive
                while (true)
                {
                    // wait for the reply
                    remote_endpoint = null;
                    receive_bytes = this.m_listen_socket.receive(receive_buffer, out remote_endpoint);
                    if (receive_bytes > 0)
                    {
                        // success in getting a message
                        break;
                    }

                    // receive error, timeout is set to 0 meaning there is no timeout
                    Console.WriteLine("CTFTPServer::run(): Receive Error");
                    Console.WriteLine("CTFTPServer::run(): Shutdown");
                    return;
                }

                // we have a valid message so figure out if read or write request, which is all we should receive here
                switch (Utilities.tftp_decode_opcode(receive_buffer))
                {
                    case EOpcode.READ_REQUEST:
                        {
                            // create a new connected client thread and start it
                            CTFTPMessageInReadRequest read_request = new CTFTPMessageInReadRequest(receive_buffer, receive_bytes);
                            CTFTPNode server = new CTFTPNode();
                            server.server_transfer_file(CTFTPNode.ETransferDirection.GET, remote_endpoint, read_request);
                        } break;
                    case EOpcode.WRITE_REQUEST:
                        {
                            // create a new connected client thread and start it
                            CTFTPMessageInWriteRequest write_request = new CTFTPMessageInWriteRequest(receive_buffer, receive_bytes);
                            CTFTPNode server = new CTFTPNode();
                            server.server_transfer_file(CTFTPNode.ETransferDirection.PUT, remote_endpoint, write_request);
                        }
                        break;
                    default:
                        {
                            // error
                            CTFTPMessageOutError error_message = new CTFTPMessageOutError(EErrorCode.ILLEGAL_OPERATION, "must first send a read or write request");
                            this.m_listen_socket.send(error_message.Buffer, error_message.BufferLength);
                        } break;
                }
            }
        }
    }
}
