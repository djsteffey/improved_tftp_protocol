

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace djs.network.tftp
{
    public class CTFTPNodeOutOfOrder
    {
        class CTransferReceiveTracker
        {
            // variables
            private byte[] m_block_completion_tracker;
            private long m_number_blocks_received;
            private long m_number_blocks_to_transfer;

            // properties
            public float PercentComplete
            {
                get { return (float)(this.m_number_blocks_received / (float)(this.m_number_blocks_to_transfer)); }
            }

            // functions
            public CTransferReceiveTracker(long tsize, ushort blksize)
            {
                // calculate the number of blocks to transfer
                this.m_number_blocks_to_transfer = (tsize / blksize) + 1;

                // create an array large enough to store 1-bit per transfer block
                // to remember if we have received the block
                this.m_block_completion_tracker = new byte[(this.m_number_blocks_to_transfer / 8) + 1];
                // block '0' is always marked as received since the first blk is actually BLK 1
                this.m_block_completion_tracker[0] = (1 << 7);

                // no blocks yet received
                this.m_number_blocks_received = 0;
            }

            public uint get_past_acks(long block_number)
            {
                // value to return
                uint past_acks = 0;

                // loop through the previous 32 block numbers
                for (int i = (int)(block_number - 1); i >= (int)(block_number - 32); --i)
                {
                    // check if it has been received
                    if (this.get_is_received(i) == true)
                    {
                        // it has so mark it as a 1
                        past_acks = past_acks << 1;
                        past_acks = past_acks | 1;
                    }
                    else
                    {
                        // it has not so mark it as a 0
                        past_acks = past_acks << 1;
                        past_acks = past_acks | 0;
                    }
                }
                return past_acks;
            }

            public bool get_is_received(long block_number)
            {
                if (block_number < 0)
                {
                    return false;
                }

                // caluclate which int in the tracker this block number belongs to
                int index = (int)(block_number / 8);

                // calculate the bit index inside of that byte
                int bit_index = (int)(8- (block_number % 8) - 1);

                // put a 1 in that bit index position
                byte checking = (byte)(1 << bit_index);

                // check if this bit is set
                if ((this.m_block_completion_tracker[index] & checking) == checking)
                {
                    // it is
                    return true;
                }
                // it isnt
                return false;
            }

            public void mark_received(long block_number)
            {
                // invalid block number
                if (block_number < 0)
                {
                    return;
                }

                // this one is already received
                if (this.get_is_received(block_number) == true)
                {
                    return;
                }

                //increment how many we have received
                this.m_number_blocks_received += 1;

                // caluclate which int in the tracker this block number belongs to
                int index = (int)(block_number / 8);

                // calculate the bit index inside of that byte
                int bit_index = (int)(8 - (block_number % 8) - 1);

                // put a 1 in that bit index position
                byte insert = (byte)(1 << bit_index);

                // OR it with the already existing value
                this.m_block_completion_tracker[index] = (byte)(this.m_block_completion_tracker[index] | insert);
            }

            public bool is_everything_received()
            {
                if (this.m_number_blocks_received == this.m_number_blocks_to_transfer)
                {
                    return true;
                }
                return false;
            }

            public override string ToString()
            {
                string s = "received: ";
                for (int i = 0; i < this.m_block_completion_tracker.Length; ++i)
                {
                    s += Convert.ToString(this.m_block_completion_tracker[i], 2) + " ";
                }
                return s;
            }
        }

        class CTransferBucketManager
        {
            // variables
            private int m_num_buckets;
            private long[] m_block_numbers;
            private Stopwatch[] m_timer_block;
            private long m_next_block;
            private long m_number_blocks_to_transfer;

            // properties
            public long[] Blocks
            {
                get { return this.m_block_numbers; }
            }
            public Stopwatch[] Timers
            {
                get { return this.m_timer_block; }
            }
            public long NextBlock
            {
                get
                {
                    return this.m_next_block;
                }
            }
            public int NumBuckets
            {
                get { return this.m_num_buckets; }
            }

            // functions
            public CTransferBucketManager(long tsize, ushort blksize, ushort num_buckets)
            {
                // save the number of buckets
                this.m_num_buckets = num_buckets;

                // create the buckets and their timers
                this.m_block_numbers = new long[this.m_num_buckets];
                this.m_timer_block = new Stopwatch[this.m_num_buckets];
                for (int i = 0; i < this.m_num_buckets; ++i)
                {
                    this.m_block_numbers[i] = -1;
                    this.m_timer_block[i] = new Stopwatch();
                }

                // the next block (first one) is going to be 1
                this.m_next_block = 1;

                // calculate the number of blocks to transfer
                this.m_number_blocks_to_transfer = (tsize / blksize) + 1;
            }

            public int get_bucket_index(long block_number)
            {
                for (int i = 0; i < this.m_num_buckets; ++i)
                {
                    if (this.m_block_numbers[i] == block_number)
                    {
                        return i;
                    }
                }
                return -1;
            }

            public bool is_all_blocks_complete()
            {
                // go through all the buckets
                for (int i = 0; i < this.m_num_buckets; ++i)
                {
                    // check their block number
                    if (this.m_block_numbers[i] != -1)
                    {
                        // if any block number is NOT -1 then we are still waiting on ACKs
                        return false;
                    }
                }
                return true;
            }

            public void advance_next_block_number()
            {
                if (this.m_next_block != -1)
                {
                    if (this.m_next_block < this.m_number_blocks_to_transfer)
                    {
                        this.m_next_block += 1;
                    }
                    else
                    {
                        // designates no more to transfer
                        this.m_next_block = -1;
                    }
                }
            }

            public override string ToString()
            {
                string s = "Next=" + this.m_next_block.ToString() + "\t Buckets=";
                for (int i = 0; i < this.m_num_buckets; ++i)
                {
                    s += this.m_block_numbers[i].ToString() + " ";
                }
                return s;
            }
        }

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
        private CTransferReceiveTracker m_transfer_receive_tracker;
        private CTransferBucketManager m_bucket_manager;
        private Stopwatch m_timer_total;
        private Stopwatch m_timer_last_ping;
        private int m_last_ping_id;
        private int m_latency_measurements;
        private double m_latency_accumulator;
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
        public CTFTPNodeOutOfOrder()
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
            this.m_transfer_receive_tracker = null;
            this.m_bucket_manager = null;
            this.m_timer_total = new Stopwatch();
            this.m_latency_measurements = 0;
            this.m_latency_accumulator = 0.0f;
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
                    }
                    break;
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
                    }
                    break;
            }

            // start ping interval timer
            this.m_timer_last_ping = new Stopwatch();
            this.m_timer_last_ping.Start();

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
                    case EOpcode.PING:
                        {
                            status = this.process_ping(buffer, bytes);
                            if (status != EStatus.OK)
                            {
                                this.m_timer_total.Stop();
                                return status;
                            }
                        }
                        break;
                    case EOpcode.PONG:
                        {
                            status = this.process_pong(buffer, bytes);
                            if (status != EStatus.OK)
                            {
                                this.m_timer_total.Stop();
                                return status;
                            }
                        }
                        break;
                }

                // time to send another ping?
                if (this.m_node_type == ENodeType.CLIENT)
                {
                    if (this.m_timer_last_ping.Elapsed.TotalSeconds >= 1.0f)
                    {
                        // every one second or so...
                        this.send_ping();
                    }
                }

                // check if any of our buckets have timed out
                // bucket manager isnt created on the server until we get the read/write request and instantiate it
                // it is not used at all on the client
                if (this.m_bucket_manager != null)
                {
                    for (int i = 0; i < this.m_bucket_manager.NumBuckets; ++i)
                    {
                        if ((this.m_bucket_manager.Timers[i].Elapsed.TotalSeconds >= this.m_timeout_in_secs) &&
                            (this.m_bucket_manager.Blocks[i] != -1))
                        {
                            // this bucket has timed out
                            // send it again
                            this.log_message("\tResending data=" + this.m_bucket_manager.Blocks[i].ToString(), ELogLevel.WARNING);
                            this.m_bucket_manager.Timers[i].Restart();
                            status = this.send_data(this.m_bucket_manager.Blocks[i]);
                            if (status != EStatus.OK)
                            {
                                return status;
                            }
                        }
                    }
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

            // doing out of order blocks
            #region region doing out of order data blocks

            // just display that we received a block
            this.log_message("process_data: received=" + message.BlockNumber + " size=" + message.DataLength.ToString(), ELogLevel.TRACE);

            // see if it is a duplicate
            if (this.m_transfer_receive_tracker.get_is_received(message.BlockNumber))
            {
                this.m_duplicates_received += 1;
                this.log_message("\tduplicate.  ignoring", ELogLevel.TRACE);
                this.m_state = EState.WAITING_DATA;
                return EStatus.OK;
            }

            // mark this block as having been received
            this.log_message("\tmarking data block received", ELogLevel.TRACE);
            this.m_transfer_receive_tracker.mark_received(message.BlockNumber);

            // send back the ACK
            uint past_acks = this.m_transfer_receive_tracker.get_past_acks(message.BlockNumber);
            this.log_message("\tsending ACK=" + message.BlockNumber.ToString() + " past=" + Convert.ToString(past_acks, 2), ELogLevel.TRACE);
            status = this.send_ack(message.BlockNumber, past_acks);

            // write the data to file
            this.write_file_bytes(this.calculate_file_position(message.BlockNumber), message.DataLength, message.Data);

            // report percentage increase?
            int percent = (int)(this.m_transfer_receive_tracker.PercentComplete * 100);
            if (percent > this.m_last_percent_reported)
            {
                this.m_last_percent_reported = percent;
                if ((this.m_last_percent_reported % 5) == 0)
                {
                    this.log_message("Percent Complete=" + this.m_last_percent_reported.ToString(), ELogLevel.MILESTONE);
                }
            }

            // in the waiting data state
            this.m_state = EState.WAITING_DATA;

            // need to check if we have now received everything
            if (this.m_transfer_receive_tracker.is_everything_received() == true)
            {
                this.m_state = EState.TRANSFER_COMPLETE;
            }

            // return the status
            return status;
            #endregion
        }

        private EStatus process_ack(byte[] buffer, int bytes)
        {
            // set the state
            this.m_state = EState.PROCESSING_ACK;

            // create the message
            CTFTPMessageInAck message = new CTFTPMessageInAck(buffer, bytes);

            // out of order
            #region out of order allowed

            // check if it is the initial ack
            if (message.BlockNumber == 0)
            {
                // log it
                this.log_message("process_ack: received=" + message.BlockNumber.ToString(), ELogLevel.TRACE);
                
                // send the first data packets up to windowsize
                for (long i = 0; i < this.m_window_size; ++i)
                {
                    long next_block = this.m_bucket_manager.NextBlock;
                    this.m_bucket_manager.advance_next_block_number();
                    this.log_message("\tSending Block=" + (next_block).ToString(), ELogLevel.TRACE);
                    this.m_bucket_manager.Blocks[i] = next_block;
                    this.m_bucket_manager.Timers[i].Start();
                    if (this.send_data(next_block) != EStatus.OK)
                    {
                        return EStatus.ERROR;
                    }
                }
                this.m_state = EState.WAITING_ACK;
                return EStatus.OK;
            }

            // get the index of the appropriate bucket
            int bucket_index = this.m_bucket_manager.get_bucket_index(message.BlockNumber);

            // show a message
            this.log_message("process_ack: received=" + message.BlockNumber.ToString() + " past=" + Convert.ToString(message.PastAcks, 2).PadLeft(32, '0') + " bucket index=" + bucket_index.ToString(), ELogLevel.TRACE);
            this.log_message("\t" + this.m_bucket_manager.ToString(), ELogLevel.TRACE);

            if (bucket_index == -1)
            {
                // this was not an ACK we were expecting
                this.log_message("\tunexpected.  ignoring", ELogLevel.TRACE);
                this.m_state = EState.WAITING_ACK;
                return EStatus.OK;
            }



            // mark this bucket as empty
            this.m_bucket_manager.Blocks[bucket_index] = -1;

            // check the past acks and use those to acknowledge anything if we can
            uint past = message.PastAcks;
            for (int i = 0; i < 32; ++i)
            {
                // extract the past ack from the message
                
                // check the LSB
                if ((past & 1) == 1)
                {
                    // this means message.BlockNumber - 32 + i? is ACKed
                    long acked_block = message.BlockNumber - 32 + i;

                    // see if this is an outstanding block
                    bucket_index = this.m_bucket_manager.get_bucket_index(acked_block);

                    // check if we got a bucket index
                    if (bucket_index != -1)
                    {
                        // it was outstanding so mark it empty now
                        this.m_bucket_manager.Blocks[bucket_index] = -1;
                    }
                }
                else
                {
                    // LSB is 0 so this ACK is anti-ACKed
                    long acked_block = message.BlockNumber - 32 + i;

                    // if it was the one just prior...go ahead and resend it
                    if (acked_block == message.BlockNumber - 1)
                    {
                        // see if this is an outstanding block
                        bucket_index = this.m_bucket_manager.get_bucket_index(acked_block);

                        // check if we got a bucket index
                        if (bucket_index != -1)
                        {
                            // resend it
                            this.log_message("\tResending data=" + (acked_block).ToString(), ELogLevel.WARNING);
                            this.m_bucket_manager.Timers[bucket_index].Restart();
                            if (this.send_data(acked_block) != EStatus.OK)
                            {
                                return EStatus.ERROR;
                            }
                        }
                    }
                }

                // shift the LSB
                past = past >> 1;
            }


            // determine if there is another to send
            // now go through all the buckets and send one where it is empty (-1)
            for (int i = 0; i < this.m_bucket_manager.NumBuckets; ++i)
            {
                // see if it is empty
                if (this.m_bucket_manager.Blocks[i] == -1)
                {
                    // it is empty
                    // get the next block to send
                    long next_block = this.m_bucket_manager.NextBlock;
                    this.m_bucket_manager.advance_next_block_number();

                    // do we have a next block to send?
                    if (next_block == -1)
                    {
                        // no more to send
                        this.log_message("\tNo additional blocks to send", ELogLevel.TRACE);

                        // check if we are done
                        if (this.m_bucket_manager.is_all_blocks_complete() == true)
                        {
                            this.log_message("Transfer Complete", ELogLevel.INFO);
                            this.m_state = EState.TRANSFER_COMPLETE;
                            return EStatus.OK;
                        }
                    }
                    else
                    {
                        // we do have another to send
                        this.log_message("\tSending Block=" + next_block.ToString(), ELogLevel.TRACE);

                        // set its block
                        this.m_bucket_manager.Blocks[i] = next_block;

                        // reset its timeout timer
                        this.m_bucket_manager.Timers[i].Restart();

                        // send it
                        this.send_data(this.m_bucket_manager.Blocks[i]);
                        this.log_message("\t" + this.m_bucket_manager.ToString(), ELogLevel.TRACE);

                        // ok
                        this.m_state = EState.WAITING_ACK;
                    }
                }
            }
            this.m_state = EState.WAITING_ACK;
            return EStatus.OK;
            #endregion
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
                                this.m_socket.TimeoutInMilliseconds = 50; // (this.m_timeout_in_secs * 1000) / 4;
                                // the actual timeout for the buckets is goverened in the bucket manager class
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

            // if out of order allowed then setup our managers
            #region out of order

            // which direction
            if (this.m_transfer_direction == ETransferDirection.GET)
            {
                // setup our receive tracker
                this.m_transfer_receive_tracker = new CTransferReceiveTracker(this.m_total_size, this.m_blksize);

                // send ACK 0 to get us started
                status = this.send_ack(0);
                if (status != EStatus.OK)
                {
                    return status;
                }
                this.m_state = EState.WAITING_DATA;
            }
            else
            {
                // setup our bucket manager
                this.m_bucket_manager = new CTransferBucketManager(this.m_total_size, this.m_blksize, this.m_window_size);

                // send the first data packets up to windowsize
                for (long i = 0; i < this.m_window_size; ++i)
                {
                    this.m_bucket_manager.Blocks[i] = i + 1;
                    this.m_bucket_manager.Timers[i].Start();
                    this.send_data(i + 1);
                }
                this.m_state = EState.WAITING_ACK;
            }
            return status;
            #endregion
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
                        }
                        break;
                    case "timeout":
                        {
                            if (this.is_option_acceptable_timeout(message.TimeoutInSecs))
                            {
                                this.m_timeout_in_secs = message.TimeoutInSecs;
                                this.m_socket.TimeoutInMilliseconds = 50; // (this.m_timeout_in_secs * 1000) / 4;
                                // the actual timeout for the buckets is goverened in the bucket manager class
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
                                
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_read_request: Unacceptable option outoforder=" + message.OutOfOrder.ToString() + ".  Sending back default=" + (true).ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                }
            }

            // out of order?
            this.m_bucket_manager = new CTransferBucketManager(this.m_total_size, this.m_blksize, this.m_window_size);

            // we now need to send back the option ack
            status = this.send_option_ack();

            // set our state
            this.m_state = EState.WAITING_OPTION_ACK_ACK;

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
                                this.m_socket.TimeoutInMilliseconds = 50; // (this.m_timeout_in_secs * 1000) / 4;
                                // the actual timeout for the buckets is goverened in the bucket manager class
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
                                
                            }
                            else
                            {
                                this.log_message("\t\t\tprocess_write_request: Unacceptable option outoforder=" + message.OutOfOrder.ToString() + ".  Sending back default=" + (true).ToString(), ELogLevel.WARNING);
                            }
                        }
                        break;
                }
            }

            // out of order?
            this.m_bucket_manager = new CTransferBucketManager(this.m_total_size, this.m_blksize, this.m_window_size);

            // we now need to send back the option ack
            status = this.send_option_ack();

            // set our state
            this.m_state = EState.WAITING_OPTION_ACK_ACK;

            // return status
            return status;
        }

        private EStatus process_ping(byte[] buffer, int bytes)
        {
            // create the message
            CTFTPMessageInPing message = new CTFTPMessageInPing(buffer, bytes);

            // show a message
            this.log_message("Received PING id=" + message.PingId.ToString() + " time=" + message.Time.ToString(), ELogLevel.TRACE);


            // create out message
            CTFTPMessageOutPong pong = new CTFTPMessageOutPong(message.Time, message.PingId);

            this.log_message("Sending PONG id=" + pong.PingId.ToString() + " time=" + pong.Time.ToString(), ELogLevel.TRACE);

            return this.send_message(pong);
        }

        private EStatus process_pong(byte[] buffer, int bytes)
        {
            // create the message
            CTFTPMessageInPong message = new CTFTPMessageInPong(buffer, bytes);

            // update latency if it matches the last ping we sent
            if (message.PingId == this.m_last_ping_id)
            {
                // show a message
                float latency = (float)(this.m_timer_total.Elapsed.TotalSeconds - message.Time);
                this.log_message("Received PONG id=" + message.PingId.ToString() + " time=" + message.Time.ToString(), ELogLevel.TRACE);
                this.log_message("\tRTT=" + (latency * 1000).ToString() + "ms", ELogLevel.TRACE);
                this.m_latency_measurements += 1;
                this.m_latency_accumulator += latency;
            }

            // ok
            return EStatus.OK;
        }

        private EStatus send_read_request()
        {
            CTFTPMessageOutReadRequest read_request_message = new CTFTPMessageOutReadRequest(this.m_filename, ETransferMode.BINARY, this.m_blksize, this.m_timeout_in_secs, this.m_total_size, this.m_window_size, true);
            return this.send_message(read_request_message);
        }

        private EStatus send_write_request()
        {
            CTFTPMessageOutWriteRequest write_request_message = new CTFTPMessageOutWriteRequest(this.m_filename, ETransferMode.BINARY, this.m_blksize, this.m_total_size, this.m_timeout_in_secs, this.m_window_size, true);
            return this.send_message(write_request_message);
        }

        private EStatus send_ack(long block_number)
        {
            // construct the message
            ushort bn = (ushort)(block_number % 65536);
            CTFTPMessageOutAck ack_message = new CTFTPMessageOutAck(bn, 0);

            // send the message
            return this.send_message(ack_message);
        }

        private EStatus send_ack(long block_number, uint past_acks)
        {
            // construct the message
            ushort bn = (ushort)(block_number % 65536);
            CTFTPMessageOutAck ack_message = new CTFTPMessageOutAck(bn, past_acks);

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
            message.add_option("outoforder", true ? "1" : "0");

            // send the message on its way
            return this.send_message(message);
        }

        private EStatus send_ping()
        {
            // reset the timer
            this.m_timer_last_ping.Restart();

            // create the message
            this.m_last_ping_id += 1;
            CTFTPMessageOutPing message = new CTFTPMessageOutPing((float)(this.m_timer_total.Elapsed.TotalSeconds), this.m_last_ping_id);

            this.log_message("Sending PING id=" + message.PingId.ToString() + " time=" + message.Time.ToString(), ELogLevel.TRACE);

            // send the message
            return this.send_message(message);
        }

        private EStatus send_pong(CTFTPMessageInPing ping_message)
        {
            // create the message
            CTFTPMessageOutPong message = new CTFTPMessageOutPong(ping_message.Time, ping_message.PingId);

            this.log_message("Sending PONG id=" + message.PingId.ToString() + " time=" + message.Time.ToString(), ELogLevel.TRACE);

            // send the message
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

                    // check resend data with the bucket timers
                    if (this.m_state == EState.WAITING_ACK)
                    {
                        // check each bucket for a timeout
                        for (int i = 0; i < this.m_bucket_manager.NumBuckets; ++i)
                        {
                            if ((this.m_bucket_manager.Timers[i].Elapsed.TotalSeconds >= this.m_timeout_in_secs) &&
                                (this.m_bucket_manager.Blocks[i] != -1))
                            {
                                // this bucket has timed out
                                // send it again
                                this.log_message("\tResending data=" + this.m_bucket_manager.Blocks[i].ToString(), ELogLevel.WARNING);
                                this.m_bucket_manager.Timers[i].Restart();
                                status = this.send_data(this.m_bucket_manager.Blocks[i]);
                                if (status != EStatus.OK)
                                {
                                    return status;
                                }
                            }
                        }
                    }

                    // check resend ack
                    else if (this.m_state == EState.WAITING_DATA)
                    {
                        // sender will timeout and resend to us
                        // but what if it is the last packet?
                        // the server could send the last to us and it get lost
                        // then the server times out but since it sent the last
                        // it thinks just the ACK back got lost and it assumes
                        // success
                    }
                    else
                    {
                        // some other general purpose timeout
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
                                if (((this.m_node_type == ENodeType.CLIENT) && (this.m_transfer_direction == ETransferDirection.PUT)) ||
                                    ((this.m_node_type == ENodeType.SERVER) && (this.m_transfer_direction == ETransferDirection.GET)))
                                {
                                    if (this.m_bucket_manager.NextBlock == -1)
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



                            // waiting for what ?
                            else
                            {
                                this.log_message("ASSERT: Unknown state=" + this.m_state.ToString(), ELogLevel.ERROR);
                                Debug.Assert(false, "Receive Timeout.  State unknown.  Do not know how to proceed");
                            }

                            // restart our timers
                            receive_timer.Restart();
                        }
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
                        Console.WriteLine(this.m_timer_total.Elapsed.ToString() + " *** WARNING ***: " + message);
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
