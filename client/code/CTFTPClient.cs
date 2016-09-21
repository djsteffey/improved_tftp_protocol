

using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace djs.network.tftp
{
    public class CTFTPClient
    {
        public enum EStatus
        {
            OK, END_OF_FILE, ERROR_BLOCK_NUMBER, ERROR_BLOCK_SIZE, ERROR_SOCKET_SEND, ERROR_UNACCEPTABLE_OPTION,
            ERROR_SOCKET_INITIALIZE, ERROR_OPEN_LOCAL_FILE, ERROR_TIMEOUT_ATTEMPTS, ERROR_ERROR_CODE_RECEIVED,
            ERROR_UNKNOWN_OPCODE
        };

        // variables
        private BinaryReader m_binary_reader;
        private BinaryWriter m_binary_writer;
        private ushort m_data_block_number;
        private uint m_data_block_number_rollovers;
        private ushort m_data_block_size;
        private uint m_data_total_size;
        private ulong m_file_bytes_sent_or_received;
        private Stopwatch m_data_transfer_timer;
        private ushort m_message_attempts;
        private CTFTPMessageOut m_message_out;
        private bool m_getting_file;
        private bool m_sending_file;
        private CSocketUdp m_socket;

        // properties


        // functions
        public CTFTPClient()
        {
            this.m_socket = new CSocketUdp(0);
        }

        public EStatus send_file(string filename, string server, int server_port)
        {
            this.m_sending_file = true;
            this.m_getting_file = false;

            // open a local file for reading in binary mode
            try
            {
                this.m_binary_reader = new BinaryReader(new FileStream(filename, FileMode.Open));
            }
            catch (Exception)
            {
                // unable to open local file for writing
                Console.WriteLine("CTFTPClient::send_file(): ERROR_OPEN_LOCAL_FILE");
                return EStatus.ERROR_OPEN_LOCAL_FILE;
            }
            this.m_binary_writer = null;

            // set the destination server and port
            this.m_socket.set_remote(server, server_port);

            // buffer for receipt of messages
            byte[] receive_buffer = new byte[65536];
            int received_bytes = 0;

            // setup parameters
            this.m_data_block_number = 1;
            this.m_data_block_number_rollovers = 0;
            this.m_data_block_size = 512;              // default value
            this.m_binary_reader.BaseStream.Seek(100, SeekOrigin.End);
            this.m_data_total_size = (uint)(this.m_binary_reader.BaseStream.Length); // (uint)(this.m_binary_reader.BaseStream.Position);
            this.m_binary_reader.BaseStream.Seek(0, SeekOrigin.Begin);
            this.m_file_bytes_sent_or_received = 0;    // total amount of file bytes sent/received so far
            this.m_message_attempts = 3;               // number of attempts to try send/receive

            // start our timing
            this.m_data_transfer_timer = new Stopwatch();
            this.m_data_transfer_timer.Start();

            // create the write request message
            this.m_message_out = new CTFTPMessageOutWriteRequest(filename, ETransferMode.BINARY, 1468, this.m_data_total_size, 5);

            // send it over the socket
            if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
            {
                // unable to send the message
                Console.WriteLine("CTFTPClient::send_file(): ERROR_SOCKET_SEND");
                return EStatus.ERROR_SOCKET_SEND;
            }

            // need to execute something first time in loop
            bool first_time_loop = true;

            // now loop until done
            while (true)
            {
                // number of times to attempt sending/receiving
                int attempts = 0;

                // try to receive
                while (true)
                {
                    // wait for the reply
                    IPEndPoint remote_endpoint = new IPEndPoint(IPAddress.Any, 0);
                    received_bytes = this.m_socket.receive(receive_buffer, ref remote_endpoint);
                    if (received_bytes > 0)
                    {
                        // success in getting a reply back
                        // the server sent the first packet back on the port it wants to receive all future messages
                        // so change the port...if this is the first message back
                        if (first_time_loop)
                        {
                            this.m_socket.set_remote(remote_endpoint);
                            first_time_loop = false;

                            // break out of the receive loop
                            break;
                        }

                        // make sure it wasnt from some other source that doesnt count
                        if (remote_endpoint.Equals(this.m_socket.RemoteEndpoint) == false)
                        {
                            // not the "real" sender we have been working with
                            // send them back an error message, not *very* concerned if it actually reaches them or not
                            CTFTPMessageOutError error_message = new CTFTPMessageOutError(EErrorCode.UNKNOWN_TRANSFER_ID, "Incorrect Source IP and/or Port");
                            this.m_socket.send(error_message.Buffer, error_message.BufferLength);

                            // this will reset our attempts since we did actually receive some data...just not from who we wanted
                            attempts = 0;
                        }
                        else
                        {
                            // received a packet from the known correct server
                            // break out of the while receive loop
                            break;
                        }
                    }

                    // timeout or other receive error
                    // increment attempts
                    ++attempts;
                    Console.WriteLine("CTFTPClient::send_file(): Receive Timeout (" + attempts.ToString() + "): ");

                    // see if over the limit
                    if (attempts >= this.m_message_attempts)
                    {
                        // too many attempts...bail out
                        Console.WriteLine("ERROR_TIMEOUT_ATTEMPTS");
                        return EStatus.ERROR_TIMEOUT_ATTEMPTS;
                    }

                    // send it over the socket again
                    Console.WriteLine("Resending");
                    if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
                    {
                        // unable to send the message
                        Console.WriteLine("CTFTPClient::send_file(): ERROR_SOCKET_SEND");
                        return EStatus.ERROR_SOCKET_SEND;
                    }

                    if (this.m_message_out.Opcode == EOpcode.DATA)
                    {
                        CTFTPMessageOutData mod = (CTFTPMessageOutData)(this.m_message_out);
                        this.m_file_bytes_sent_or_received += (ulong)(mod.DataLength);
                    }
                }

                // message_out got delivered and received response

                // switch on the opcode
                switch (Utilities.tftp_decode_opcode(receive_buffer))
                {
                    case EOpcode.READ_REQUEST:
                        {
                            // should not receive this on the client
                            // ignore it
                        }
                        break;
                    case EOpcode.WRITE_REQUEST:
                        {
                            // should not receive this on the client
                            // ignore it
                        }
                        break;
                    case EOpcode.DATA:
                        {
                            // will not receive this on a send_file
                            // ignore
                        }
                        break;
                    case EOpcode.ACK:
                        {
                            // received the ack from the server, send the next data block
                            // a block of the file is received
                            EStatus status = this.process_message_ack(receive_buffer, received_bytes);
                            if (status == EStatus.END_OF_FILE)
                            {
                                // end of file....success!!!!
                                // OMFG !!!!
                                // close the file and return true we are complete
                                this.m_socket.close();
                                this.m_binary_reader.Close();

                                // stop our timer
                                this.m_data_transfer_timer.Stop();
                                Console.WriteLine("Transfer Complete");
                                Console.WriteLine("Transfer took " + this.m_data_transfer_timer.Elapsed.TotalSeconds.ToString() + " seconds for " + (this.m_data_total_size / 1024.0f).ToString() + " KB (" + (this.m_file_bytes_sent_or_received / 1024.0f).ToString() + ")");
                                Console.WriteLine("Transfer rate: " + (this.m_file_bytes_sent_or_received / 1024.0f / this.m_data_transfer_timer.Elapsed.TotalSeconds).ToString() + " KB/sec");
                                return EStatus.END_OF_FILE;
                            }
                            else if (status == EStatus.ERROR_BLOCK_SIZE)
                            {
                                // fail
                                Console.WriteLine("CTFTPClient::send_file(): ERROR_BLOCK_SIZE");
                                return EStatus.ERROR_BLOCK_SIZE;
                            }
                            else if (status == EStatus.ERROR_SOCKET_SEND)
                            {
                                // we were unable to send a packet out on the wire
                                // just fail
                                Console.WriteLine("CTFTPClient::send_file(): ERROR_SOCKET_SEND");
                                return EStatus.ERROR_SOCKET_SEND;
                            }
                        }
                        break;
                    case EOpcode.ERROR:
                        {
                            // some error...decode it
                            EErrorCode error_code = EErrorCode.UNDEFINED;
                            string error_string = "";
                            this.process_message_error(receive_buffer, received_bytes, out error_code, out error_string);
                            Console.WriteLine("CTFTPClient::send_file(): ERROR_CODE_RECEIVED: code=" + Utilities.tftp_error_code_to_string(error_code) + " string=" + error_string);
                            return EStatus.ERROR_ERROR_CODE_RECEIVED;
                        }
                        break;
                    case EOpcode.OPTION_ACK:
                        {
                            if (this.process_message_option_ack(receive_buffer, received_bytes) != EStatus.OK)
                            {
                                // some kind of problem...didnt accept the servers ACK options
                                Console.WriteLine("CTFTPClient::send_file(): ERROR_UNACCEPTABLE_OPTION");
                                return EStatus.ERROR_UNACCEPTABLE_OPTION;
                            }
                        }
                        break;
                    default:
                        {
                            // unknown OPCODE
                            this.process_message_unknown(receive_buffer, received_bytes);

                            // possible garbled data came in
                            Console.WriteLine("CTFTPClient::send_file(): ERROR_UNKNOWN_OPCODE");
                            return EStatus.ERROR_UNKNOWN_OPCODE;
                        }
                        break;
                }
            }
            // should never be able to get here
            return EStatus.OK;
        }

        public EStatus get_file(string filename, string server_name, int server_port)
        {
            this.m_getting_file = true;
            this.m_sending_file = false;

            // open a local file for writing in binary mode and truncate it
            try
            {
                this.m_binary_writer = new BinaryWriter(new FileStream(filename, FileMode.Create));
            }
            catch (Exception)
            {
                // unable to open local file for writing
                Console.WriteLine("CTFTPClient::get_file(): ERROR_OPEN_LOCAL_FILE");
                return EStatus.ERROR_OPEN_LOCAL_FILE;
            }
            this.m_binary_reader = null;

            // set the destination server and port
            this.m_socket.set_remote(server_name, server_port);

            // buffer for receipt of messages
            byte[] receive_buffer = new byte[65536];
            int received_bytes = 0;

            // setup parameters
            this.m_data_block_number = 1;
            this.m_data_block_number_rollovers = 0;
            this.m_data_block_size = 512;              // default value
            this.m_data_total_size = 0;                // size of file (0 is unknown)
            this.m_file_bytes_sent_or_received = 0;    // total amount of file bytes sent/received so far
            this.m_message_attempts = 3;               // number of attempts to try send/receive

            // start our timing
            this.m_data_transfer_timer = new Stopwatch();
            this.m_data_transfer_timer.Start();

            // create the read request message
            this.m_message_out = new CTFTPMessageOutReadRequest(filename, ETransferMode.BINARY, 1468, 5);

            // send it over the socket
            if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
            {
                // unable to send the message
                return EStatus.ERROR_SOCKET_SEND;
            }

            // need to execute something first time in loop
            bool first_time_loop = true;

            // now loop until done
            while (true)
            {
                // number of times to attempt sending/receiving
                int attempts = 0;

                // try to receive
                while (true)
                {
                    // wait for the reply
                    IPEndPoint remote_endpoint = new IPEndPoint(IPAddress.Any, 0);
                    received_bytes = this.m_socket.receive(receive_buffer, ref remote_endpoint);
                    if (received_bytes > 0)
                    {
                        // success in getting a reply back
                        // the server sent the first packet back on the port it wants to receive all future messages
                        // so change the port...if this is the first message back
                        if (first_time_loop)
                        {
                            this.m_socket.set_remote(remote_endpoint);
                            first_time_loop = false;

                            // break out of the receive loop
                            break;
                        }

                        // make sure it wasnt from some other source that doesnt count
                        if (remote_endpoint.Equals(this.m_socket.RemoteEndpoint) == false)
                        {
                            // not the "real" sender we have been working with
                            // send them back an error message, not *very* concerned if it actually reaches them or not
                            CTFTPMessageOutError error_message = new CTFTPMessageOutError(EErrorCode.UNKNOWN_TRANSFER_ID, "Incorrect Source IP and/or Port");
                            this.m_socket.send(error_message.Buffer, error_message.BufferLength);

                            // this will reset our attempts since we did actually receive some data...just not from who we wanted
                            attempts = 0;
                        }
                        else
                        {
                            // received a packet from the known correct server
                            // break out of the while receive loop
                            break;
                        }
                    }

                    // timeout or other receive error
                    // increment attempts
                    ++attempts;
                    Console.WriteLine("Timeout:  Attempts: " + attempts.ToString());

                    // see if over the limit
                    if (attempts >= this.m_message_attempts)
                    {
                        // too many attempts...bail out
                        Console.WriteLine("too many....bail out");
                        return EStatus.ERROR_TIMEOUT_ATTEMPTS;
                    }

                    // send it over the socket again
                    Console.WriteLine("Resending");
                    if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
                    {
                        // unable to send the message
                        return EStatus.ERROR_SOCKET_SEND;
                    }
                }

                // message_out got delivered and received response

                // switch on the opcode
                switch (Utilities.tftp_decode_opcode(receive_buffer))
                {
                    case EOpcode.READ_REQUEST:
                        {
                            // should not receive this on the client
                            // ignore it
                        }
                        break;
                    case EOpcode.WRITE_REQUEST:
                        {
                            // should not receive this on the client
                            // ignore it
                        }
                        break;
                    case EOpcode.DATA:
                        {
                            // a block of the file is received
                            EStatus status = this.process_message_data(receive_buffer, received_bytes);
                            if (status == EStatus.END_OF_FILE)
                            {
                                // end of file....success!!!!
                                // OMFG !!!!
                                // close the file and return true we are complete
                                this.m_socket.close();
                                this.m_binary_writer.Close();

                                // stop our timer
                                this.m_data_transfer_timer.Stop();
                                Console.WriteLine("Transfer Complete");
                                Console.WriteLine("Transfer took " + this.m_data_transfer_timer.Elapsed.TotalSeconds.ToString() + " seconds for " + this.m_file_bytes_sent_or_received.ToString() + " bytes");
                                Console.WriteLine("Transfer rate: " + (this.m_file_bytes_sent_or_received / 1024.0f / this.m_data_transfer_timer.Elapsed.TotalSeconds).ToString() + " KB/sec");
                                return EStatus.END_OF_FILE;
                            }
                            else if (status == EStatus.ERROR_BLOCK_SIZE)
                            {
                                // fail
                                return EStatus.ERROR_BLOCK_SIZE;
                            }
                            else if (status == EStatus.ERROR_SOCKET_SEND)
                            {
                                // we were unable to send a packet out on the wire
                                // just fail
                                return EStatus.ERROR_SOCKET_SEND;
                            }
                        }
                        break;
                    case EOpcode.ACK:
                        {
                            // should not receive this on the client
                            // ignore it
                        }
                        break;
                    case EOpcode.ERROR:
                        {
                            // some error...decode it
                            EErrorCode error_code = EErrorCode.UNDEFINED;
                            string error_string = "";
                            this.process_message_error(receive_buffer, received_bytes, out error_code, out error_string);
                            Console.WriteLine("CTFTPClient::get_file(): ERROR_CODE_RECEIVED: code=" + Utilities.tftp_error_code_to_string(error_code) + " string=" + error_string);
                            return EStatus.ERROR_ERROR_CODE_RECEIVED;
                        }
                        break;
                    case EOpcode.OPTION_ACK:
                        {
                            if (this.process_message_option_ack(receive_buffer, received_bytes) != EStatus.OK)
                            {
                                // some kind of problem...didnt accept the servers ACK options
                                return EStatus.ERROR_UNACCEPTABLE_OPTION;
                            }
                        }
                        break;
                    default:
                        {
                            // unknown OPCODE
                            this.process_message_unknown(receive_buffer, received_bytes);

                            // possible garbled data came in
                            return EStatus.ERROR_UNKNOWN_OPCODE;
                        }
                        break;
                }
            }

            // should never be able to get here
            return EStatus.OK;
        }

        private EStatus process_message_data(byte[] buffer, int size)
        {
            // create the message
            CTFTPMessageInData message = new CTFTPMessageInData(buffer, size);

            // check if it is the expected data block
            if (message.BlockNumber != this.m_data_block_number)
            {
                // even if it isnt the expected one, it was still data over the wire so count it as transmitted bytes
                this.m_file_bytes_sent_or_received += message.DataLength;

                if (message.BlockNumber < this.m_data_block_number)
                {
                    // received an older one...ignore it
                    Console.WriteLine("process_message_data(): received older block number\tExpected: " + this.m_data_block_number.ToString() + "\tGot: " + message.BlockNumber.ToString() + "\tIgnoring it");
                }
                else
                {
                    Console.WriteLine("process_message_data(): received wrong block number\tExpected: " + this.m_data_block_number.ToString() + "\tGot: " + message.BlockNumber.ToString() + "\tResending ACK");

                    // send ack of last block received to facilitate the sender to send the next packet
                    this.m_message_out = new CTFTPMessageOutAck((ushort)(this.m_data_block_number - 1));

                    // send it out over the socket
                    if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
                    {
                        // unable to send
                        return EStatus.ERROR_SOCKET_SEND;
                    }
                }
                // block number error...but can still recover
                return EStatus.ERROR_BLOCK_NUMBER;
            }

            // correct chunk..check the bytes
            if (message.DataLength <= this.m_data_block_size)
            {
                // increment how many bytes received thus far
                this.m_file_bytes_sent_or_received += message.DataLength;

                // send back an ack
                this.m_message_out = new CTFTPMessageOutAck(this.m_data_block_number);
                if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
                {
                    return EStatus.ERROR_SOCKET_SEND;
                }

                if (this.m_data_block_number % 1 == 0)
                {
                    Console.WriteLine("Received data block.  Size: " + message.DataLength.ToString() + "\tRollover: " + this.m_data_block_number_rollovers.ToString() + "\tBlock: " + this.m_data_block_number.ToString() + "\tTotal Bytes: " + this.m_file_bytes_sent_or_received.ToString());
                }

                // write data to file
                // seek to the position for this specific block
                // we use this instead of the m_file_bytes_sent_or_received as in my future
                // proposal will be to allow out of order packets
                uint file_position = ((this.m_data_block_number_rollovers * 65536) + this.m_data_block_number - 1) * this.m_data_block_size;
                this.m_binary_writer.BaseStream.Seek(file_position, SeekOrigin.Begin);

                // write the data
                this.m_binary_writer.Write(message.Data, 0, (int)(message.DataLength));

                // increment data block number...but watch out for rollover
                if (this.m_data_block_number == 65535)
                {
                    this.m_data_block_number = 0;
                    ++(this.m_data_block_number_rollovers);
                }
                else
                {
                    ++(this.m_data_block_number);
                }

                // check if it was less than a full block
                if (message.DataLength < this.m_data_block_size)
                {
                    // this means the file is complete
                    return EStatus.END_OF_FILE;
                }

                // not complete but still going strong
                return EStatus.OK;
            }
            else
            {
                Console.WriteLine("process_message_data(): received invalid number of bytes\tGot: " + message.DataLength.ToString());
                // some number of bytes that is not ok with us...over the data_block_size
                // send back an error message
                this.m_message_out = new CTFTPMessageOutError(EErrorCode.ILLEGAL_OPERATION, "block size incorrect");
                if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
                {
                    return EStatus.ERROR_SOCKET_SEND;
                }
                return EStatus.ERROR_BLOCK_SIZE;
            }

            // shouldnt get here
            return EStatus.OK;
        }

        private EStatus process_message_option_ack(byte[] buffer, int size)
        {
            // decode the message
            CTFTPMessageInOptionAck message = new CTFTPMessageInOptionAck(buffer, size);

            // check and see if the options is what we can handle
            if ((message.BlockSize >= 512) && (message.BlockSize <= 65504))
            {
                this.m_data_block_size = message.BlockSize;
            }
            else
            {
                // todo send error packet to server
                return EStatus.ERROR_UNACCEPTABLE_OPTION;
            }

            // make sure we can handle the total size
            // maybe check for disc space...for now just take it
            // todo check if we want the file with the given size
            if (this.m_getting_file)
            {
                this.m_data_total_size = message.TotalSize;
            }
            else if (this.m_sending_file)
            {
                // server should have returned this to us as a 0 because it doesnt know the size
            }

            // check timeout value
            if ((message.TimeoutInSecs <= 0) || (message.TimeoutInSecs > 60))
            {
                // timeout doesnt work for us
                // todo send error message to server
                return EStatus.ERROR_UNACCEPTABLE_OPTION;
            }
            // set our sockets timeout value
            this.m_socket.TimeoutInMilliseconds = (message.TimeoutInSecs * 1000);

            if (this.m_getting_file)
            {
                // send the ACK to the server that we accept and wait for the server to send data
                this.m_message_out = new CTFTPMessageOutAck(0);
                if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
                {
                    return EStatus.ERROR_SOCKET_SEND;
                }
            }
            else if (this.m_sending_file)
            {
                // send the first data packet

                // seek to the beginning of the file for block 1
                uint file_position = 0;
                this.m_binary_reader.BaseStream.Seek(file_position, SeekOrigin.Begin);

                // read the data
                ushort num_bytes = this.m_data_block_size;
                if (num_bytes > this.m_data_total_size - file_position)
                {
                    num_bytes = (ushort)(this.m_data_total_size - file_position);
                }
                byte[] file_bytes = this.m_binary_reader.ReadBytes((int)num_bytes);

                // send it to the server
                this.m_data_block_number = 1;
                this.m_message_out = new CTFTPMessageOutData(this.m_data_block_number, file_bytes, num_bytes);
                if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
                {
                    return EStatus.ERROR_SOCKET_SEND;
                }

                // add up our bytes
                // this will be higher than the actual file size if there are retransmits
                // but will accurately capture how many file bytes were actually sent
                this.m_file_bytes_sent_or_received += num_bytes;
            }
            return EStatus.OK;
        }

        private EStatus process_message_ack(byte[] buffer, int size)
        {
            // create the message
            CTFTPMessageInAck message = new CTFTPMessageInAck(buffer, size);

            Console.WriteLine("process_message_ack(): received ACK: " + message.BlockNumber.ToString());

            // see if this was the final ACK
            uint last_block_number = (this.m_data_total_size / this.m_data_block_size) + 1;
            uint last_rollover = last_block_number / 65536;
            last_block_number %= 65536;
            if ((message.BlockNumber == last_block_number) && (last_rollover == this.m_data_block_number_rollovers))
            {
                // we are finished
                return EStatus.END_OF_FILE;
            }

            // the next block to send is the ACK number + 1....but watch for rollover
            if (message.BlockNumber == 65535)
            {
                this.m_data_block_number = 0;
                ++(this.m_data_block_number_rollovers);
            }
            else
            {
                this.m_data_block_number = (ushort)(message.BlockNumber + 1);
            }

            // fetch the next amount of data
            // seek to the position for this specific block
            // we use this instead of the m_file_bytes_sent_or_received as in my future
            // proposal will be to allow out of order packets
            uint file_position = ((this.m_data_block_number_rollovers * 65536) + this.m_data_block_number - 1) * this.m_data_block_size;
            this.m_binary_reader.BaseStream.Seek(file_position, SeekOrigin.Begin);

            // read the data
            ushort num_bytes = this.m_data_block_size;
            if (num_bytes > this.m_data_total_size - file_position)
            {
                num_bytes = (ushort)(this.m_data_total_size - file_position);
            }
            byte[] file_bytes = this.m_binary_reader.ReadBytes((int)num_bytes);

            // send it to the server
            this.m_message_out = new CTFTPMessageOutData(this.m_data_block_number, file_bytes, num_bytes);
            if (this.m_socket.send(this.m_message_out.Buffer, this.m_message_out.BufferLength) == false)
            {
                return EStatus.ERROR_SOCKET_SEND;
            }
            Console.WriteLine("sending block number: " + this.m_data_block_number.ToString());

            // add up our bytes
            // this will be higher than the actual file size if there are retransmits
            // but will accurately capture how many file bytes were actually sent
            this.m_file_bytes_sent_or_received += num_bytes;

            // success
            return EStatus.OK;
        }

        private EStatus process_message_error(byte[] buffer, int size, out EErrorCode error_code, out string error_string)
        {
            // decode the message
            CTFTPMessageInError message = new CTFTPMessageInError(buffer, size);

            // get the vars back to the caller
            error_code = message.ErrorCode;
            error_string = message.ErrorString;

            // just return ok....calling function will decide what to do based on the error_code
            return EStatus.OK;
        }

        private EStatus process_message_unknown(byte[] buffer, int size)
        {
            // for now do nothing
            return EStatus.OK;
        }
    }
}
