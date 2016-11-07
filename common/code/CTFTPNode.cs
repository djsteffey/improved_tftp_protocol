

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace djs.network.tftp
{
    public class CTFTPNode
    {
        public enum ENodeType { UNDEFINED, SERVER, CLIENT };
        public enum ETransferDirection { UNDEFINED, GET, PUT };
        private enum ELogLevel { INFO, WARNING, ERROR, STATISTICS, TRACE, MILESTONE };
        public enum EStatus
        {
            UNDEFINED, OK, ERROR
        };
        private enum EState
        {
            UNDEFINED, WAITING_READ_REQUEST_RESPONSE, WAITING_WRITE_REQUEST_RESPONSE, WAITING_OPTION_ACK_ACK, WAITING_ACK, WAITING_DATA,
            PROCESSING_READ_REQUEST, PROCESSING_WRITE_REQUEST, PROCESSING_OPTION_ACK, PROCESSING_ERROR, PROCESSING_ACK, PROCESSING_DATA,
            TRANSFER_COMPLETE
        };


        // variables
        private ENodeType m_node_type;
        private EState m_state;
        private CSocketUdp m_socket;
        private ETransferDirection m_transfer_direction;
        private string m_filename;
        private ushort m_blksize;
        private uint m_total_size;
        private ushort m_timeout_in_secs;
        private ushort m_window_size;
        private bool m_out_of_order;
        private Stopwatch m_timer_total;
        private long m_next_expected_faux;
        private Stopwatch m_timer_latency;
        private int m_latency_measurements;
        private double m_latency_accumulator;
        private long m_last_received_block;
        private long m_next_expected_block;
        private Stopwatch m_timer_last_correct;
        private long m_last_block;
        private int m_window_size_received;
        private uint m_duplicates_received;
        private int m_last_percent_reported;

        // properties
        public long TotalSize
        {
            get { return this.m_total_size; }
        }
        public double TotalTime
        {
            get { return this.m_timer_total.Elapsed.TotalSeconds; }
        }
        public int LatencyMeasurements
        {
            get { return this.m_latency_measurements; }
        }
        public double LatencyAccumulator
        {
            get { return this.m_latency_accumulator; }
        }
        public uint Duplicates
        {
            get { return this.m_duplicates_received; }
        }
        public CSocketUdp Socket
        {
            get { return this.m_socket; }
        }

        // functions
        public CTFTPNode()
        {
            this.m_node_type = ENodeType.UNDEFINED;
            this.m_state = EState.UNDEFINED;
            this.m_socket = new CSocketUdp(0);
            this.m_transfer_direction = ETransferDirection.UNDEFINED;
            this.m_filename = "";
            this.m_blksize = 512;
            this.m_total_size = 0;
            this.m_timeout_in_secs = 3;
            this.m_window_size = 1;
            this.m_out_of_order = false;
            this.m_timer_total = new Stopwatch();
            this.m_next_expected_faux = 0;
            this.m_timer_latency = new Stopwatch();
            this.m_latency_measurements = 0;
            this.m_latency_accumulator = 0.0f;
            this.m_last_received_block = 0;
            this.m_next_expected_block = 1;
            this.m_timer_last_correct = new Stopwatch();
            this.m_last_block = 0;
            this.m_window_size_received = 0;
            this.m_duplicates_received = 0;
            this.m_last_percent_reported = 0;
        }

        public EStatus client_transfer_file(ETransferDirection direction, string remote_name, int remote_port, string filename, ushort blksize, ushort timeout_in_secs, uint total_size, ushort windowsize, float drop_chance, bool out_of_order)
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // start the timer
            this.m_timer_total.Start();

            // setup variables
            this.m_node_type = ENodeType.CLIENT;
            this.m_transfer_direction = direction;
            this.m_filename = filename;
            this.m_blksize = blksize;
            this.m_timeout_in_secs = timeout_in_secs;
            this.m_total_size = total_size;
            this.m_window_size = windowsize;
            this.m_socket.set_remote(remote_name, remote_port);


            // send read or write request
            switch (this.m_transfer_direction)
            {
                case ETransferDirection.GET:
                    {
                        this.log_message("client_transfer_file:  Sending read request", ELogLevel.INFO);
                        status = this.send_read_request();
                        if (status == EStatus.ERROR)
                        {
                            this.m_timer_total.Stop();

                            return status;
                        }
                        this.m_state = EState.WAITING_READ_REQUEST_RESPONSE;
                    }break;
                case ETransferDirection.PUT:
                    {
                        this.log_message("client_transfer_file:  Sending write request", ELogLevel.INFO);
                        status = this.send_write_request();
                        if (status == EStatus.ERROR)
                        {
                            this.m_timer_total.Stop();
                            return status;
                        }
                        this.m_state = EState.WAITING_WRITE_REQUEST_RESPONSE;
                    }
                    break;
                default:
                    {
                        Debug.Assert(false, "client_transfer_file: UNDEFINED transfer direction");
                    }break;
            }

            // setup our socket to purposeful drop the given percent of packets
            this.m_socket.SimulatedDropChanceSend = drop_chance;
            this.m_socket.SimulatedDropChanceReceive = drop_chance;

            // run the loop
            status = this.transfer_loop();

            // return the status
            this.m_timer_total.Stop();
            return status;
        }

        public EStatus server_transfer_file(ETransferDirection direction, IPEndPoint end_point, CTFTPMessageIn message)
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // start the timer
            this.m_timer_total.Start();

            // setup variables
            this.m_node_type = ENodeType.SERVER;
            this.m_transfer_direction = direction;
            this.m_socket.set_remote(end_point);

            // process the message as a read or write request
            switch (this.m_transfer_direction)
            {
                case ETransferDirection.GET:
                    {
                        this.log_message("server_transfer_file:  Processing read request", ELogLevel.INFO);
                        status = this.process_read_request(message as CTFTPMessageInReadRequest);
                        if (status == EStatus.ERROR)
                        {
                            return status;
                        }
                    }
                    break;
                case ETransferDirection.PUT:
                    {
                        this.log_message("server_transfer_file:  Processing write request", ELogLevel.INFO);
                        status = this.process_write_request(message as CTFTPMessageInWriteRequest);
                        if (status == EStatus.ERROR)
                        {
                            return status;
                        }
                    }
                    break;
                default:
                    {
                        Debug.Assert(false, "server_transfer_file: UNDEFINED transfer direction");
                    }
                    break;
            }

            // run the loop
            return this.transfer_loop();
        }

        private EStatus transfer_loop()
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // buffer for receiving messages
            byte[] buffer = new byte[65536];
            int bytes = 0;

            // need to execute special code on first message received
            bool first = true;

            while (true)
            {
                // receive a message
                status = this.receive_message(first, buffer, out bytes);

                // check if a received message resulted in a complete transfer
                if (this.m_state == EState.TRANSFER_COMPLETE)
                {
                    this.m_timer_total.Stop();
                    return EStatus.OK;
                }

                // check if we got a message
                if (status != EStatus.OK)
                {
                    // timed out and too many attempts
                    this.m_timer_total.Stop();
                    return EStatus.ERROR;
                }

                // switch on the opcode
                switch (Utilities.tftp_decode_opcode(buffer))
                {
                    case EOpcode.ACK:
                        {
                            status = this.process_ack(buffer, bytes);
                            if (status != EStatus.OK)
                            {
                                this.m_timer_total.Stop();
                                return status;
                            }
                            if (this.m_state == EState.TRANSFER_COMPLETE)
                            {
                                this.m_timer_total.Stop();
                                return EStatus.OK;
                            }
                        }
                        break;
                    case EOpcode.DATA:
                        {
                            status = this.process_data(buffer, bytes);
                            if (status != EStatus.OK)
                            {
                                this.m_timer_total.Stop();
                                return status;
                            }
                            if (this.m_state == EState.TRANSFER_COMPLETE)
                            {
                                this.m_timer_total.Stop();
                                return EStatus.OK;
                            }
                        }
                        break;
                    case EOpcode.ERROR:
                        {
                            status = this.process_error(buffer, bytes);
                            if (status != EStatus.OK)
                            {
                                this.m_timer_total.Stop();
                                return status;
                            }
                        }
                        break;
                    case EOpcode.OPTION_ACK:
                        {
                            status = this.process_option_ack(buffer, bytes);
                            if (status != EStatus.OK)
                            {
                                this.m_timer_total.Stop();
                                return status;
                            }
                        }
                        break;
                    case EOpcode.READ_REQUEST:
                        {
                            status = this.process_read_request(null);
                            if (status != EStatus.OK)
                            {
                                this.m_timer_total.Stop();
                                return status;
                            }
                        }
                        break;
                    case EOpcode.WRITE_REQUEST:
                        {
                            status = this.process_write_request(null);
                            if (status != EStatus.OK)
                            {
                                this.m_timer_total.Stop();
                                return status;
                            }
                        }
                        break;
                }
            }
        }

        private EStatus process_data(byte[] buffer, int bytes)
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // set the state
            this.m_state = EState.PROCESSING_DATA;

            // create the message
            CTFTPMessageInData message = new CTFTPMessageInData(buffer, bytes);

            // log a message of receiving a data packet
            this.log_message("process_data: received=" + message.BlockNumber + " expected=" + this.m_next_expected_block.ToString() + " size=" + message.DataLength.ToString(), ELogLevel.TRACE);

            // determine if it is the correct message
            if (message.BlockNumber == this.m_next_expected_block)
            {
                // correct one we were expecting
                // record the latency if it was the first one back after an ACK
                if (this.m_window_size_received == 0)
                {
                    this.m_latency_measurements += 1;
                    this.m_latency_accumulator += this.m_timer_latency.Elapsed.TotalSeconds;
                    this.log_message("\tLatency=" + (this.m_timer_latency.Elapsed.TotalSeconds * 1000).ToString(), ELogLevel.TRACE);
                }

                // save the data to the file
                this.write_file_bytes(this.calculate_file_position(message.BlockNumber), message.DataLength, message.Data);

                // update our last received and next expected
                this.m_last_received_block = message.BlockNumber;
                this.m_next_expected_block = this.m_next_expected_block + 1;
                this.m_next_expected_faux = this.m_next_expected_block;

                // increment the received window size
                this.m_window_size_received += 1;

                // determine if we have received enough windowsize packets to warrant sending back an ACK
                if ((this.m_window_size_received == this.m_window_size) | (message.DataLength < this.m_blksize))
                {
                    // received windowsize qty of packets OR it was the last packet
                    // so time to send the ACK
                    // log the message
                    this.log_message("\tsending ACK=" + this.m_last_received_block.ToString(), ELogLevel.TRACE);

                    // reset received window
                    this.m_window_size_received = 0;
                    
                    // send the ACK
                    status = this.send_ack(this.m_last_received_block);
                    if (status != EStatus.OK)
                    {
                        return status;
                    }
                }

                // report percentage increase?
                int percent = (int)((message.BlockNumber / (float)this.m_last_block) * 100);
                if (percent > this.m_last_percent_reported)
                {
                    this.m_last_percent_reported = percent;
                    if ((this.m_last_percent_reported % 5) == 0)
                    {
                        this.log_message("Percent Complete=" + this.m_last_percent_reported.ToString(), ELogLevel.MILESTONE);
                    }
                }


                // check if we are done
                if (message.DataLength < this.m_blksize)
                {
                    // last one
                    // log the message
                    this.log_message("process_data:  Transfer Complete", ELogLevel.INFO);

                    // set our state to complete
                    this.m_state = EState.TRANSFER_COMPLETE;

                    // return status
                    return EStatus.OK;
                }

                // restart timers
                this.m_timer_last_correct.Restart();
                this.m_timer_latency.Restart();

                // update state
                status = EStatus.OK;
                this.m_state = EState.WAITING_DATA;
            }
            else if (message.BlockNumber <= this.m_last_received_block)
            {
                // log that this is a duplicate received
                this.log_message("\trecevied duplicate=" + message.BlockNumber.ToString(), ELogLevel.WARNING);

                // increment our duplicate counter
                this.m_duplicates_received += 1;

                // detect if there is a drop in the duplicates
                if (this.m_next_expected_faux == this.m_next_expected_block)
                {
                    // first duplicate in this series of duplicates
                    // so set our false next expected to this block number + 1
                    this.m_next_expected_faux = message.BlockNumber + 1;
                }
                else
                {
                    // calculate the qty of drops
                    // which is this messages block number - our false expected
                    uint qty = (uint)(message.BlockNumber - this.m_next_expected_faux);

                    // update our false expected
                    this.m_next_expected_faux = message.BlockNumber + 1;

                    // log a message for number of drops
                    this.log_message("\tDetected qty=" + qty.ToString() + " drops", ELogLevel.WARNING);
                }


                // we have already received this DATA
                // only send the ACK if we are in a timeout
                if (this.m_timer_last_correct.Elapsed.TotalSeconds >= this.m_timeout_in_secs)
                {
                    this.log_message("\timeout.  resending ACK=" + this.m_last_received_block.ToString(), ELogLevel.WARNING);
                    this.m_window_size_received = 0;
                    status = this.send_ack(this.m_last_received_block);
                    if (status != EStatus.OK)
                    {
                        return status;
                    }

                    // restart timers
                    this.m_timer_last_correct.Restart();
                    this.m_timer_latency.Restart();
                }
                this.m_state = EState.WAITING_DATA;
                status = EStatus.OK;
            }
            else if (message.BlockNumber > this.m_next_expected_block)
            {
                // greater than what we expected
                // this will happen when windowsize > 1 and there was a drop
                // log a message
                this.log_message("\trecevied 'future'=" + message.BlockNumber.ToString(), ELogLevel.WARNING);

                // any time a future message is received, it WILL be resent again by the sender once the client informs them
                // there was a drop
                this.m_duplicates_received += 1;

                // figure out the drops
                if (message.BlockNumber > this.m_next_expected_faux)
                {
                    // the qty of drops is the number received - the one expected
                    uint qty = (uint)(message.BlockNumber - this.m_next_expected_faux);

                    // log a message
                    this.log_message("\tDetected qty=" + qty.ToString() + " drops", ELogLevel.WARNING);

                    // update the false next expected
                    this.m_next_expected_faux = message.BlockNumber + 1;
                }
                else
                {
                    // no drops so just update the false next expected
                    this.m_next_expected_faux = message.BlockNumber + 1;
                }

                // anytime we get a future message we must send an ACK with the last one correctly received
                this.log_message("\tResending ACK=" + this.m_last_received_block.ToString(), ELogLevel.WARNING);

                // reset the received window size
                this.m_window_size_received = 0;

                // send the ack
                status = this.send_ack(this.m_last_received_block);
                if (status != EStatus.OK)
                {
                    return status;
                }

                // reset the timers
                this.m_timer_latency.Restart();
                this.m_timer_last_correct.Restart();

                // update the status and state
                status = EStatus.OK;
                this.m_state = EState.WAITING_DATA;
            }
            // return our status
            return status;
        }

        private EStatus process_ack(byte[] buffer, int bytes)
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // set the state
            this.m_state = EState.PROCESSING_ACK;

            // create the message
            CTFTPMessageInAck message = new CTFTPMessageInAck(buffer, bytes);

            this.log_message("process_ack: received=" + message.BlockNumber + " expected=" + this.m_next_expected_block.ToString(), ELogLevel.TRACE);

            // determine if it is the correct message
            if (message.BlockNumber == this.m_next_expected_block)
            {
                // correct one
                this.m_latency_measurements += 1;
                this.m_latency_accumulator += this.m_timer_latency.Elapsed.TotalSeconds;
                this.log_message("\tLatency=" + (this.m_timer_latency.Elapsed.TotalSeconds * 1000).ToString(), ELogLevel.TRACE);

                // check if we are done sending data
                if (message.BlockNumber == this.m_last_block)
                {
                    // we are done
                    this.log_message("process_ack:  Transfer Complete", ELogLevel.INFO);
                    this.m_state = EState.TRANSFER_COMPLETE;
                    return EStatus.OK;
                }

                // send the next data for the next window amount
                this.m_last_received_block = message.BlockNumber;
                this.m_next_expected_block = Math.Min(this.m_last_received_block + this.m_window_size, this.m_last_block);
                status = this.send_data_range(this.m_last_received_block + 1, this.m_next_expected_block);
                if (status != EStatus.OK)
                {
                    return status;
                }

                // restart timers
                this.m_timer_last_correct.Restart();
                this.m_timer_latency.Restart();

                this.log_message("\tsending next data start=" + (this.m_last_received_block + 1).ToString() + " end=" + this.m_next_expected_block.ToString(), ELogLevel.TRACE);

                this.m_state = EState.WAITING_ACK;
            }
            else if (message.BlockNumber <= this.m_last_received_block)
            {
                this.log_message("\trecevied duplicate=" + message.BlockNumber.ToString(), ELogLevel.WARNING);

                // we have already received this ACK
                // only send the next data if we are in a timeout
                if (this.m_timer_last_correct.Elapsed.TotalSeconds >= this.m_timeout_in_secs)
                {
                    this.log_message("\timeout.  resending data start=" + (this.m_last_received_block + 1).ToString() + " end=" + this.m_next_expected_block.ToString(), ELogLevel.WARNING);

                    // send the next data for the next window amount
                    status = this.send_data_range(this.m_last_received_block + 1, this.m_next_expected_block);
                    if (status != EStatus.OK)
                    {
                        return status;
                    }

                    // restart timers
                    this.m_timer_last_correct.Restart();
                    this.m_timer_latency.Restart();
                }

                this.m_state = EState.WAITING_ACK;

                status = EStatus.OK;
            }
            else if ((message.BlockNumber > this.m_last_received_block) & (message.BlockNumber < this.m_next_expected_block))
            {
                // between
                // this will come up with windosize > 1
                // send a new windowsize of data greater than this block number
                this.log_message("\trecevied update ACK=" + message.BlockNumber.ToString() + " (but less than expected)", ELogLevel.WARNING);
                
                // send the next data for the next window amount
                this.m_last_received_block = message.BlockNumber;
                this.m_next_expected_block = Math.Min(this.m_last_received_block + this.m_window_size, this.m_last_block);
                status = this.send_data_range(this.m_last_received_block + 1, this.m_next_expected_block);
                if (status != EStatus.OK)
                {
                    return status;
                }
                this.m_timer_latency.Restart();
                this.m_timer_last_correct.Restart();

                this.log_message("\tsending next data start=" + (this.m_last_received_block + 1).ToString() + " end=" + this.m_next_expected_block.ToString(), ELogLevel.TRACE);

                this.m_state = EState.WAITING_ACK;
                status = EStatus.OK;
            }
            else
            {
                // received ACK greater than a data block we have sent....something really wrong
                Debug.Assert(false, "process_ack: Shouldn't receive an ACK=" + message.BlockNumber.ToString() + " greater than the next expected=" + this.m_next_expected_block.ToString());
            }

            return status;
        }

        private EStatus process_error(byte[] buffer, int bytes)
        {
            // set the state
            this.m_state = EState.PROCESSING_ERROR;

            // create the message
            CTFTPMessageInError message = new CTFTPMessageInError(buffer, bytes);

            // log it
            this.log_message("process_error: " + message.ErrorCode.ToString() + ": " + message.ErrorString, ELogLevel.ERROR);

            // return status
            return EStatus.ERROR;
        }

        private EStatus process_option_ack(byte[] buffer, int bytes)
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // set our state
            this.m_state = EState.PROCESSING_OPTION_ACK;

            // create the message
            CTFTPMessageInOptionAck message = new CTFTPMessageInOptionAck(buffer, bytes);

            // show a message
            this.log_message("Received Option ACK:", ELogLevel.INFO);

            // this will only happen on the client side
            if (this.m_node_type != ENodeType.CLIENT)
            {
                this.log_message("\tprocess_option_ack:  This message only valid for a client node", ELogLevel.ERROR);
                this.send_error(EErrorCode.ILLEGAL_OPERATION, "Server doesnt accept OPTION ACK messages");
                return EStatus.ERROR;
            }

            // check the options to make sure we support
            this.log_message("\toptions", ELogLevel.INFO);
            foreach (var kvp in message.Options)
            {
                this.log_message("\t\t" + kvp.Key + " = " + kvp.Value, ELogLevel.INFO);
                switch (kvp.Key)
                {
                    case "blksize":
                        {
                            if (this.is_option_acceptable_blksize(message.BlockSize))
                            {
                                this.m_blksize = message.BlockSize;
                                this.m_last_block = (this.m_total_size / this.m_blksize) + 1;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_option_ack: Unacceptable option blksize=" + message.BlockSize.ToString(), ELogLevel.ERROR);
                                this.send_error(EErrorCode.TERMINATE_OPTIONS, "Unacceptable option " + kvp.Key + "=" + kvp.Value);
                                return EStatus.ERROR;
                            }
                        }
                        break;
                    case "tsize":
                        {
                            if (this.is_option_acceptable_tsize(message.TotalSize))
                            {
                                this.m_total_size = message.TotalSize;
                                this.m_last_block = (this.m_total_size / this.m_blksize) + 1;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_option_ack: Unacceptable option tsize=" + message.TotalSize.ToString(), ELogLevel.ERROR);
                                this.send_error(EErrorCode.TERMINATE_OPTIONS, "Unacceptable option " + kvp.Key + "=" + kvp.Value);
                                return EStatus.ERROR;
                            }
                        }
                        break;
                    case "timeout":
                        {
                            if (this.is_option_acceptable_timeout(message.TimeoutInSecs))
                            {
                                this.m_timeout_in_secs = message.TimeoutInSecs;
                                this.m_socket.TimeoutInMilliseconds = this.m_timeout_in_secs * 1000;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_option_ack: Unacceptable option timeout=" + message.TimeoutInSecs.ToString(), ELogLevel.ERROR);
                                this.send_error(EErrorCode.TERMINATE_OPTIONS, "Unacceptable option " + kvp.Key + "=" + kvp.Value);
                                return EStatus.ERROR;
                            }
                        }
                        break;
                    case "windowsize":
                        {
                            if (this.is_option_acceptable_windowsize(message.WindowSize))
                            {
                                this.m_window_size = message.WindowSize;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_option_ack: Unacceptable option windowsize=" + message.WindowSize.ToString(), ELogLevel.ERROR);
                                this.send_error(EErrorCode.TERMINATE_OPTIONS, "Unacceptable option " + kvp.Key + "=" + kvp.Value);
                                return EStatus.ERROR;
                            }
                        }
                        break;
                    case "outoforder":
                        {
                            if (this.is_option_acceptable_outoforder(message.OutOfOrder))
                            {
                                this.m_out_of_order = message.OutOfOrder;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_option_ack: Unacceptable option outoforder=" + message.OutOfOrder.ToString(), ELogLevel.ERROR);
                                this.send_error(EErrorCode.TERMINATE_OPTIONS, "Unacceptable option " + kvp.Key + "=" + kvp.Value);
                                return EStatus.ERROR;
                            }
                        }
                        break;
                }
            }

            // we now need to send the first data packet if it is a write request
            // or send an ACK 0 if it is a read request
            switch (this.m_transfer_direction)
            {
                case ETransferDirection.GET:
                    {
                        // send the ACK 0 to tell the sender to start
                        this.m_last_received_block = 0;
                        this.m_next_expected_block = 1;
                        status = this.send_ack(this.m_last_received_block);
                        if (status != EStatus.OK)
                        {
                            return status;
                        }
                        this.m_state = EState.WAITING_DATA;
                    } break;
                case ETransferDirection.PUT:
                    {
                        // send the first data
                        // we havent received any yet
                        this.m_last_received_block = 0;
                        // the next ACK we expect to get is the window size
                        // because that is how many we are initially sending
                        this.m_next_expected_block = Math.Min(this.m_window_size, this.m_last_block);
                        
                        // send that range
                        status = this.send_data_range(1, this.m_next_expected_block);
                        if (status != EStatus.OK)
                        {
                            return status;
                        }
                        this.m_state = EState.WAITING_ACK;
                    } break;
                default:
                    {
                        Debug.Assert(false, "process_option_ack: Transfer direction is UNDEFINED");
                    } break;
            }

            // restart the timers
            this.m_timer_last_correct.Restart();
            this.m_timer_latency.Restart();

            // return status
            return status;
        }

        private EStatus process_read_request(CTFTPMessageInReadRequest message)
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // set our state
            this.m_state = EState.PROCESSING_READ_REQUEST;

            // show a message
            this.log_message("Received Read Request:", ELogLevel.INFO);

            // this will only happen on the server side
            if (this.m_node_type != ENodeType.SERVER)
            {
                this.log_message("\tprocess_read_request:  This message only valid for a server node", ELogLevel.ERROR);
                this.send_error(EErrorCode.ILLEGAL_OPERATION, "Client doesnt accept READ REQUEST messages");
                return EStatus.ERROR;
            }

            // check the filename, transfer mode, and options to make sure we support
            if (this.is_send_file_acceptable(message.Filename) == false)
            {
                this.log_message("\tprocess_read_request:  Unable to send filename=" + message.Filename, ELogLevel.ERROR);
                this.send_error(EErrorCode.ACCESS_VIOLATION, "Server unable to send filename=" + message.Filename);
                return EStatus.ERROR;
            }
            this.m_filename = message.Filename;
            this.log_message("\tfilename = " + this.m_filename, ELogLevel.INFO);

            if (this.is_transfer_mode_acceptable(message.TransferMode) == false)
            {
                this.log_message("\tprocess_read_request:  Unable to accept transfermode=" + message.TransferMode.ToString(), ELogLevel.ERROR);
                this.send_error(EErrorCode.TERMINATE_OPTIONS, "Unacceptable transfer mode.  Only acceptable is BINARY");
                return EStatus.ERROR;
            }
            this.log_message("\tmode = " + ETransferMode.BINARY.ToString(), ELogLevel.INFO);

            this.log_message("\toptions", ELogLevel.INFO);
            foreach (var kvp in message.Options)
            {
                this.log_message("\t\t" + kvp.Key + " = " + kvp.Value, ELogLevel.INFO);
                switch (kvp.Key)
                {
                    case "blksize":
                        {
                            if (this.is_option_acceptable_blksize(message.BlockSize))
                            {
                                this.m_blksize = message.BlockSize;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_read_request: Unacceptable option blksize=" + message.BlockSize.ToString() + ".  Sending back default=" + this.m_blksize.ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                    case "tsize":
                        {
                            // we have to be the server if receiving the read request
                            // the client doesnt know the size of the file but this option
                            // was sent with a value of 0 so that the server can fill in
                            // the value and send it back
                            // so we do not overwrite our own total size value
                            // since we arent using files we can just put the value here
                            // of the size of 'file' that we want to emulate sending
                            // but the way we are operating for our testing is the client
                            // will tell us how much they want to send
                            // this helps in automating the test
                            this.m_total_size = message.TotalSize;
                            this.m_last_block = (this.m_total_size / this.m_blksize) + 1;
                        }
                        break;
                    case "timeout":
                        {
                            if (this.is_option_acceptable_timeout(message.TimeoutInSecs))
                            {
                                this.m_timeout_in_secs = message.TimeoutInSecs;
                                this.m_socket.TimeoutInMilliseconds = this.m_timeout_in_secs * 1000;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_read_request: Unacceptable option timeout=" + message.TimeoutInSecs.ToString() + ".  Sending back default=" + this.m_timeout_in_secs.ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                    case "windowsize":
                        {
                            if (this.is_option_acceptable_windowsize(message.WindowSize))
                            {
                                this.m_window_size = message.WindowSize;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_read_request: Unacceptable option windowsize=" + message.WindowSize.ToString() + ".  Sending back default=" + this.m_window_size.ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                    case "outoforder":
                        {
                            if (this.is_option_acceptable_outoforder(message.OutOfOrder))
                            {
                                this.m_out_of_order = message.OutOfOrder;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_read_request: Unacceptable option outoforder=" + message.OutOfOrder.ToString() + ".  Sending back default=" + this.m_out_of_order.ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                }
            }

            // we now need to send back the option ack
            status = this.send_option_ack();

            // setup what we expect to receive
            this.m_last_received_block = -1;
            this.m_next_expected_block = 0;         // indicates we need to wait for ACK 0 back from client after they get our OPTION ACK

            // set our state
            this.m_state = EState.WAITING_OPTION_ACK_ACK;

            // start our last correct timer and latency
            this.m_timer_last_correct.Restart();
            this.m_timer_latency.Restart();

            // return status
            return status;
        }

        private EStatus process_write_request(CTFTPMessageInWriteRequest message)
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // set our state
            this.m_state = EState.PROCESSING_WRITE_REQUEST;

            // show a message
            this.log_message("Received Write Request:", ELogLevel.INFO);

            // this will only happen on the server side
            if (this.m_node_type != ENodeType.SERVER)
            {
                this.log_message("\tprocess_write_request:  This message only valid for a server node", ELogLevel.ERROR);
                this.send_error(EErrorCode.ILLEGAL_OPERATION, "Client doesnt accept WRITE REQUEST messages");
                return EStatus.ERROR;
            }

            // check the filename, transfer mode, and options to make sure we support
            if (this.is_receive_file_acceptable(message.Filename) == false)
            {
                this.log_message("\tprocess_write_request:  Unable to accept filename=" + message.Filename, ELogLevel.ERROR);
                this.send_error(EErrorCode.ACCESS_VIOLATION, "Server unable to receive filename=" + message.Filename);
                return EStatus.ERROR;
            }
            this.m_filename = message.Filename;
            this.log_message("\tfilename = " + this.m_filename, ELogLevel.INFO);

            if (this.is_transfer_mode_acceptable(message.TransferMode) == false)
            {
                this.log_message("\tprocess_write_request:  Unable to accept transfermode=" + message.TransferMode.ToString(), ELogLevel.ERROR);
                this.send_error(EErrorCode.TERMINATE_OPTIONS, "Unacceptable transfer mode.  Only acceptable is BINARY");
                return EStatus.ERROR;
            }
            this.log_message("\tmode = " + ETransferMode.BINARY.ToString(), ELogLevel.INFO);

            this.log_message("\toptions", ELogLevel.INFO);
            foreach (var kvp in message.Options)
            {
                this.log_message("\t\t" + kvp.Key + " = " + kvp.Value, ELogLevel.INFO);
                switch (kvp.Key)
                {
                    case "blksize":
                        {
                            if (this.is_option_acceptable_blksize(message.BlockSize))
                            {
                                this.m_blksize = message.BlockSize;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_write_request: Unacceptable option blksize=" + message.BlockSize.ToString() + ".  Sending back default=" + this.m_blksize.ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                    case "tsize":
                        {
                            if (this.is_option_acceptable_tsize(message.TotalSize))
                            {
                                this.m_total_size = message.TotalSize;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_write_request: Unacceptable option tsize=" + message.TotalSize.ToString(), ELogLevel.ERROR);
                                return EStatus.ERROR;
                            }
                        }
                        break;
                    case "timeout":
                        {
                            if (this.is_option_acceptable_timeout(message.TimeoutInSecs))
                            {
                                this.m_timeout_in_secs = message.TimeoutInSecs;
                                this.m_socket.TimeoutInMilliseconds = this.m_timeout_in_secs * 1000;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_write_request: Unacceptable option timeout=" + message.TimeoutInSecs.ToString() + ".  Sending back default=" + this.m_timeout_in_secs.ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                    case "windowsize":
                        {
                            if (this.is_option_acceptable_windowsize(message.WindowSize))
                            {
                                this.m_window_size = message.WindowSize;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_write_request: Unacceptable option windowsize=" + message.WindowSize.ToString() + ".  Sending back default=" + this.m_window_size.ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                    case "outoforder":
                        {
                            if (this.is_option_acceptable_outoforder(message.OutOfOrder))
                            {
                                this.m_out_of_order = message.OutOfOrder;
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_write_request: Unacceptable option outoforder=" + message.OutOfOrder.ToString() + ".  Sending back default=" + this.m_out_of_order.ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                }
            }

            // we now need to send back the option ack
            status = this.send_option_ack();

            // set our state
            this.m_state = EState.WAITING_OPTION_ACK_ACK;

            // setup what we expect to receive
            this.m_last_received_block = 0;
            this.m_next_expected_block = 1;

            // start our last correct timer
            this.m_timer_last_correct.Restart();
            this.m_timer_latency.Restart();

            // return status
            return status;
        }

        private EStatus send_read_request()
        {
            CTFTPMessageOutReadRequest read_request_message = new CTFTPMessageOutReadRequest(this.m_filename, ETransferMode.BINARY, this.m_blksize, this.m_timeout_in_secs, this.m_total_size, this.m_window_size, this.m_out_of_order);
            return this.send_message(read_request_message);
        }

        private EStatus send_write_request()
        {
            CTFTPMessageOutWriteRequest write_request_message = new CTFTPMessageOutWriteRequest(this.m_filename, ETransferMode.BINARY, this.m_blksize, this.m_total_size, this.m_timeout_in_secs, this.m_window_size, this.m_out_of_order);
            return this.send_message(write_request_message);
        }

        private EStatus send_ack(long block_number)
        {
            // construct the message
            ushort bn = (ushort)(block_number % 65536);
            CTFTPMessageOutAck ack_message = new CTFTPMessageOutAck(bn);

            // send the message
            return this.send_message(ack_message);
        }

        private EStatus send_data(long block_number)
        {
            // calculate the file offset
            long position = this.calculate_file_position(block_number);

            // calculate number of bytes
            // this is the minimum of the block size and how many bytes are left to send
            ushort num_bytes = (ushort)(Math.Min(this.m_blksize, this.m_total_size - position));

            // get the bytes to send
            byte[] bytes_to_send = this.read_file_bytes(position, num_bytes);

            // construct the message
            ushort bn = (ushort)(block_number % 65536);
            CTFTPMessageOutData data_message = new CTFTPMessageOutData(bn, bytes_to_send, num_bytes);

            // send the message
            return this.send_message(data_message);
        }

        private EStatus send_data_range(long start, long end)
        {
            EStatus status = EStatus.UNDEFINED;
            for (long bn = start; bn <= end; ++bn)
            {
                status = this.send_data(bn);
                if (status != EStatus.OK)
                {
                    return status;
                }
            }
            return EStatus.OK;
        }

        private EStatus send_error(EErrorCode error_code, string error_string, IPEndPoint end_point = null)
        {
            // create the message
            CTFTPMessageOutError message = new CTFTPMessageOutError(error_code, error_string);

            // send the message
            return this.send_message(message, end_point);
        }

        private EStatus send_option_ack()
        {
            // create the message
            CTFTPMessageOutOptionAck message = new CTFTPMessageOutOptionAck();

            // put the options into it
            message.add_option("blksize", this.m_blksize.ToString());
            message.add_option("tsize", this.m_total_size.ToString());
            message.add_option("timeout", this.m_timeout_in_secs.ToString());
            message.add_option("windowsize", this.m_window_size.ToString());
            message.add_option("outoforder", "1");

            // send the message on its way
            return this.send_message(message);
        }

        private EStatus receive_message(bool first, byte[] receive_buffer, out int receive_bytes)
        {
            // status
            EStatus status = EStatus.UNDEFINED;

            // keep track of how many times we have tried
            int num_attempts = 0;

            // used for detecting timeout when receiving packets from non-valid sources
            Stopwatch receive_timer = new Stopwatch();

            // loop forever, or at least until return out of the loop
            while (true)
            {
                // start the reciever timer
                receive_timer.Restart();

                // loop until we timeout or get a packet from our true sender
                while (true)
                {
                    // endpoint messsage received from
                    IPEndPoint remote_endpoint = null;

                    // receive the bytes
                    receive_bytes = this.m_socket.receive(receive_buffer, out remote_endpoint);

                    // detect if we got bytes or error
                    if (receive_bytes > 0)
                    {
                        // success in getting a reply back
                        // the server sent the first packet back on the port it wants to receive all future messages
                        // so change the port...if this is the first message back
                        if (first)
                        {
                            this.m_socket.set_remote(remote_endpoint);
                            return EStatus.OK;
                        }

                        // make sure it wasnt from some other source that doesnt count
                        if (remote_endpoint.Equals(this.m_socket.RemoteEndpoint) == true)
                        {
                            // received a packet from the known correct server
                            return EStatus.OK;
                        }
                        else
                        {
                            // not the "real" sender we have been working with
                            // send them back an error message, not *very* concerned if it actually reaches them or not
                            this.send_error(EErrorCode.UNKNOWN_TRANSFER_ID, "Incorrect Source IP and/or Port", remote_endpoint);
                        }
                    }

                    // if we get to this point then it means either the socket timed out while receiving
                    // or we received a message but not from our true sender...we can check for both by
                    // checking the receiver_timer
                    if (receive_timer.Elapsed.TotalSeconds >= this.m_timeout_in_secs)
                    {
                        // we do indeed have a timeout so handle it
                        this.log_message("receive_message:  Timeout", ELogLevel.WARNING);

                        // increment the number of times we have tried
                        num_attempts += 1;

                        // check for too many
                        if (num_attempts >= 3)
                        {
                            // too many attempts...
                            // but see if we did send the last data packet
                            if (((this.m_node_type == ENodeType.CLIENT) && ( this.m_transfer_direction == ETransferDirection.PUT)) ||
                                ((this.m_node_type == ENodeType.SERVER) && (this.m_transfer_direction == ETransferDirection.GET)))
                            {
                                long last_block = (this.m_total_size / this.m_blksize) + 1;
                                if (this.m_next_expected_block == last_block)
                                {
                                    // we may have timed out and ran out of attempts
                                    // but we did actually send out the last data packet
                                    // it could be likely (very) that the other side received the last packet
                                    // but their ACK back got lost, and they only ACK back the final DATABLK
                                    // one time and then exit
                                    // if this is the case (very likely) then the transfer did succeed
                                    this.log_message("receive_message:  Timeout.  Over max number of attempts=" + num_attempts.ToString() +
                                        ". However we did send the last DATABLK.  Highly likely the other side received and their reply ACK got lost. Highly likely the transfer is complete.",
                                        ELogLevel.ERROR);
                                    this.m_state = EState.TRANSFER_COMPLETE;
                                    return EStatus.OK;
                                }
                            }

                            this.log_message("receive_message:  Timeout.  Over max number of attempts=" + num_attempts.ToString(), ELogLevel.ERROR);
                            return EStatus.ERROR;
                        }

                        // check resend read request
                        if (this.m_state == EState.WAITING_READ_REQUEST_RESPONSE)
                        {
                            this.log_message("\tResending read request", ELogLevel.WARNING);
                            status = this.send_read_request();
                            if (status != EStatus.OK)
                            {
                                return status;
                            }
                        }

                        // check resend write request
                        else if (this.m_state == EState.WAITING_WRITE_REQUEST_RESPONSE)
                        {
                            this.log_message("\tResending write request", ELogLevel.WARNING);
                            status = this.send_write_request();
                            if (status != EStatus.OK)
                            {
                                return status;
                            }
                        }

                        // check resend option ack
                        else if (this.m_state == EState.WAITING_OPTION_ACK_ACK)
                        {
                            this.log_message("\tResending option ack", ELogLevel.WARNING);
                            status = this.send_option_ack();
                            if (status != EStatus.OK)
                            {
                                return status;
                            }
                        }

                        // check resend data
                        else if (this.m_state == EState.WAITING_ACK)
                        {
                            this.log_message("\tsending next data start=" + (this.m_last_received_block + 1).ToString() + " end=" + this.m_next_expected_block.ToString(), ELogLevel.TRACE);
                            status = this.send_data_range(this.m_last_received_block + 1, this.m_next_expected_block);
                            if (status != EStatus.OK)
                            {
                                return status;
                            }
                        }

                        // check resend ack
                        else if (this.m_state == EState.WAITING_DATA)
                        {
                            this.log_message("\tResending ACK=" + this.m_last_received_block.ToString(), ELogLevel.WARNING);
                            status = this.send_ack(this.m_last_received_block);
                            this.m_window_size_received = 0;
                            if (status != EStatus.OK)
                            {
                                return status;
                            }
                        }

                        // waiting for what ?
                        else
                        {
                            this.log_message("ASSERT: Unknown state=" + this.m_state.ToString(), ELogLevel.ERROR);
                            Debug.Assert(false, "Receive Timeout.  State unknown.  Do not know how to proceed");
                        }

                        // restart our timers
                        receive_timer.Restart();
                        this.m_timer_last_correct.Restart();
                        this.m_timer_latency.Restart();
                    }
                }
            }
        }

        private EStatus send_message(CTFTPMessageOut message, IPEndPoint end_point = null)
        {
            // status
            bool success = false;

            // numer of attempts
            int attempts = 0;

            // loop it
            while (true)
            {
                if (end_point == null)
                {
                    success = this.m_socket.send(message.Buffer, message.BufferLength);
                }
                else
                {
                    success = this.m_socket.send(message.Buffer, message.BufferLength, end_point);
                }

                if (success)
                {
                    // gtg
                    return EStatus.OK;
                }

                // unable to send...very likely because we are trying to send messages too fast
                this.log_message("send_message(): Unable to send message", ELogLevel.WARNING);

                // increment attempts
                attempts += 1;

                // see if too many attempts
                if (attempts >= 3)
                {
                    // too many attempts
                    this.log_message("send_message(): Over max number send attempts", ELogLevel.ERROR);

                    return EStatus.ERROR;
                }

                // do a slight pause for the given number of milliseconds
                this.log_message("send_message(): Pausing before trying again", ELogLevel.WARNING);

                Thread.Sleep(1);
            }
        }

        private byte[] read_file_bytes(long offset, ushort count)
        {
            // no "file" yet at this time
            // TODO:  can probably speed up by keeping a buffer at the class
            // level and allocate once and use the same buffer every time
            // instead of allocating here every time
            return new byte[count];
        }

        private void write_file_bytes(long offset, ushort count, byte[] bytes)
        {
            // dont do anything with it at this time
        }

        private long calculate_file_position(long block_number)
        {
            // calculate the offset to the start of bytes for the given block
            return (block_number - 1) * this.m_blksize;
        }

        private bool is_option_acceptable_blksize(ushort blksize)
        {
            // accept any
            return true;
        }

        private bool is_option_acceptable_tsize(long tsize)
        {
            // accept any
            return true;
        }

        private bool is_option_acceptable_timeout(ushort timeout_in_secs)
        {
            // accept any
            return true;
        }

        private bool is_option_acceptable_windowsize(ushort windowsize)
        {
            // accept any
            return true;
        }

        private bool is_option_acceptable_outoforder(bool out_of_order)
        {
            // accept any
            return true;
        }

        private bool is_receive_file_acceptable(string filename)
        {
            return true;
        }

        private bool is_send_file_acceptable(string filename)
        {
            return true;
        }

        private bool is_transfer_mode_acceptable(ETransferMode mode)
        {
            switch (mode)
            {
                case ETransferMode.ASCII: { return false; }
                case ETransferMode.BINARY: { return true; }
                default: { Debug.Assert(false, "is_transfer_mode_acceptable:  UNDEFINED transfer mode"); } break;
            }
            return false;
        }

        private void log_message(string message, ELogLevel level)
        {
            switch (level)
            {
                case ELogLevel.ERROR:
                    {
                        Console.WriteLine(this.m_timer_total.Elapsed.ToString() + " *** ERROR ***: " + message);
                    }
                    break;
                case ELogLevel.INFO:
                    {
                        Console.WriteLine(this.m_timer_total.Elapsed.ToString() + " *** INFO ***: " + message);
                    }
                    break;
                case ELogLevel.STATISTICS:
                    {
                        Console.WriteLine("*** STATISTICS ***: " + message);
                    }
                    break;
                case ELogLevel.TRACE:
                    {
//                        Console.WriteLine(this.m_timer_total.Elapsed.ToString() + " *** TRACE ***: " + message);
                    }
                    break;
                case ELogLevel.MILESTONE:
                    {
                        Console.WriteLine("*** MILESTONE ***: " + message);
                    }
                    break;
                case ELogLevel.WARNING:
                    {
//                        Console.WriteLine(this.m_timer_total.Elapsed.ToString() + " *** WARNING ***: " + message);
                    }
                    break;
                default:
                    {
                        Console.WriteLine(this.m_timer_total.Elapsed.ToString() + " *** UNKNOWN ***: " + message);
                    }
                    break;
            }
        }
    }
}
