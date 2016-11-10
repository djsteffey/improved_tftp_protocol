
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace djs.network.tftp
{
    public enum ETransferMode { UNDEFINED, BINARY, ASCII };

    public enum EOpcode { READ_REQUEST = 1, WRITE_REQUEST = 2, DATA = 3, ACK = 4, ERROR = 5, OPTION_ACK = 6, PING = 7, PONG = 8 };

    public enum EErrorCode
    {
        UNDEFINED = 0, FILE_NOT_FOUND = 1, ACCESS_VIOLATION = 2, DISK_FULL = 3, ILLEGAL_OPERATION = 4,
        UNKNOWN_TRANSFER_ID = 5, FILE_ALREADY_EXISTS = 6, NO_SUCH_USER = 7, TERMINATE_OPTIONS = 8, NONE = 9
    };

    public static class Utilities
    {
        public static ETransferMode string_to_tftp_mode(string s)
        {
            // make lower case for comparing
            s = s.ToLower();

            switch (s)
            {
                case "netascii": { return ETransferMode.ASCII; }
                case "octet": { return ETransferMode.BINARY; }
            }
            return ETransferMode.UNDEFINED;
        }

        public static string tftp_mode_to_string(ETransferMode m)
        {
            switch (m)
            {
                case ETransferMode.ASCII: { return "netascii"; };
                case ETransferMode.BINARY: { return "octet"; };
            }
            return "unknown";
        }

        public static string tftp_error_code_to_string(EErrorCode error_code)
        {
            switch (error_code)
            {
                case EErrorCode.UNDEFINED: { return "TFTP_ERROR_UNDEFINED"; }
                case EErrorCode.ACCESS_VIOLATION: { return "TFTP_ERROR_ACCESS_VIOLATION"; }
                case EErrorCode.DISK_FULL: { return "TFTP_ERROR_DISK_FULL"; }
                case EErrorCode.FILE_ALREADY_EXISTS: { return "TFTP_ERROR_FILE_ALREADY_EXIST"; }
                case EErrorCode.FILE_NOT_FOUND: { return "TFTP_ERROR_FILE_NOT_FOUND"; }
                case EErrorCode.ILLEGAL_OPERATION: { return "TFTP_ERROR_ILLEGAL_OPERATION"; }
                case EErrorCode.NO_SUCH_USER: { return "TFTP_ERROR_NO_SUCH_USER"; }
                case EErrorCode.TERMINATE_OPTIONS: { return "TFTP_ERROR_TERMINATE_OPTIONS"; }
                case EErrorCode.UNKNOWN_TRANSFER_ID: { return "TFTP_ERROR_UNKNOWN_TRANSFER_ID"; }
                
            }
            return "TFTP_ERROR_UNKNOWN";
        }

        public static EOpcode tftp_decode_opcode(byte[] buffer)
        {
            ushort opcode = BitConverter.ToUInt16(buffer, 0);
            opcode = Utilities.ntohs(opcode);
            return (EOpcode)opcode;
        }

        public static ushort htons(ushort value)
        {
            value = (ushort)(IPAddress.HostToNetworkOrder((short)(value)));
            return value;
        }

        public static ushort ntohs(ushort value)
        {
            value = (ushort)(IPAddress.NetworkToHostOrder((short)(value)));
            return value;
        }

        public static int htoni(int value)
        {
            value = IPAddress.HostToNetworkOrder(value);
            return value;
        }

        public static int ntohi(int value)
        {
            value = IPAddress.NetworkToHostOrder(value);
            return value;
        }
    }

    public abstract class CTFTPMessageOut
    {
        // variables
        protected byte[] m_buffer;
        protected int m_buffer_length;

        // properties
        public abstract EOpcode Opcode
        {
            get;
        }
        public byte[] Buffer
        {
            get { return this.m_buffer; }
        }
        public int BufferLength
        {
            get { return this.m_buffer_length; }
        }

        // functions
        public CTFTPMessageOut()
        {
            this.m_buffer = null;
            this.m_buffer_length = 0;
        }
    }

    public class CTFTPMessageOutWriteRequest : CTFTPMessageOut
    {
        // variables
        private string m_filename;
        private ETransferMode m_mode;
        private ushort m_block_size;
        private long m_total_size;
        private ushort m_timeout_in_secs;
        private ushort m_window_size;
        private bool m_out_of_order;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.WRITE_REQUEST; }
        }

        // functions
        public CTFTPMessageOutWriteRequest(string filename, ETransferMode mode, ushort block_size, long total_size, ushort timeout_in_secs, ushort window_size, bool out_of_order)
        {
            // save the passed variables
            this.m_filename = filename;
            this.m_mode = mode;
            this.m_block_size = block_size;
            this.m_total_size = total_size;
            this.m_timeout_in_secs = timeout_in_secs;
            this.m_window_size = window_size;
            this.m_out_of_order = out_of_order;

            // allocate the buffer space...estimate the size needed based on length of the filename
            // plus room for opcode, mode, blocksize, timeout, extra padding
            // since this is a one time packet we can "over pad" it without much detriment
            // the actual final length will be calculated at the end to store in m_buffer_length
            this.m_buffer = new byte[filename.Length + 256];

            // create the buffer for this message and put all the data in it
            int buffer_index = 0;
            string temp = "";

            // set the opcode
            ushort opcode = Utilities.htons((ushort)(this.Opcode));
            Array.Copy(BitConverter.GetBytes(opcode), 0, this.m_buffer, buffer_index, 2);
            buffer_index += 2;

            // set the filename
            Array.Copy(Encoding.ASCII.GetBytes(this.m_filename), 0, this.m_buffer, buffer_index, this.m_filename.Length);
            buffer_index += filename.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // set the mode
            temp = Utilities.tftp_mode_to_string(mode);
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // terminate it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // set a block size
            // the string blksize
            temp = "blksize";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // now the value
            temp = block_size.ToString();
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // tsize option
            temp = "tsize";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;
            // now the value
            temp = total_size.ToString();
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // timeout value
            temp = "timeout";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;
            // now the value
            temp = timeout_in_secs.ToString();
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // windowsize value
            temp = "windowsize";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;
            // now the value
            temp = this.m_window_size.ToString();
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // buckets value
            temp = "outoforder";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;
            // now the value
            if (this.m_out_of_order == true)
            {
                temp = "1";
            }
            else
            {
                temp = "0";
            }
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // now save the buffer length
            this.m_buffer_length = buffer_index;
        }
    }

    public class CTFTPMessageOutReadRequest : CTFTPMessageOut
    {
        // variables
        private string m_filename;
        private ETransferMode m_mode;
        private ushort m_block_size;
        private uint m_total_size;
        private ushort m_timeout_in_secs;
        private ushort m_window_size;
        private bool m_out_of_order;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.READ_REQUEST; }
        }

        // functions
        public CTFTPMessageOutReadRequest(string filename, ETransferMode mode, ushort block_size, ushort timeout_in_secs, uint tsize, ushort windowsize, bool out_of_order)
        {
            // save the passed variables
            this.m_filename = filename;
            this.m_mode = mode;
            this.m_block_size = block_size;
            this.m_total_size = tsize;
            this.m_timeout_in_secs = timeout_in_secs;
            this.m_window_size = windowsize;
            this.m_out_of_order = out_of_order;

            // allocate the buffer space...estimate the size needed based on length of the filename
            // plus room for opcode, mode, blocksize, timeout, extra padding
            // since this is a one time packet we can "over pad" it without much detriment
            // the actual final length will be calculated at the end to store in m_buffer_length
            this.m_buffer = new byte[filename.Length + 256];

            // create the buffer for this message and put all the data in it
            int buffer_index = 0;
            string temp = "";

            // set the opcode
            ushort opcode = Utilities.htons((ushort)(this.Opcode));
            Array.Copy(BitConverter.GetBytes(opcode), 0, this.m_buffer, buffer_index, 2);
            buffer_index += 2;

            // set the filename
            Array.Copy(Encoding.ASCII.GetBytes(this.m_filename), 0, this.m_buffer, buffer_index, this.m_filename.Length);
            buffer_index += filename.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // set the mode
            temp = Utilities.tftp_mode_to_string(mode);
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // terminate it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // set a block size
            // the string blksize
            temp = "blksize";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // now the value
            temp = block_size.ToString();
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // tsize option
            temp = "tsize";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // now the value
            temp = this.m_total_size.ToString();
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // timeout value
            temp = "timeout";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;
            // now the value
            temp = timeout_in_secs.ToString();
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // windowsize value
            temp = "windowsize";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;
            // now the value
            temp = this.m_window_size.ToString();
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // buckets value
            temp = "outoforder";
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;
            // now the value
            if (out_of_order == true)
            {
                temp = "1";
            }
            else
            {
                temp = "0";
            }
            Array.Copy(Encoding.ASCII.GetBytes(temp), 0, this.m_buffer, buffer_index, temp.Length);
            buffer_index += temp.Length;
            // termiante it with 0
            this.m_buffer[buffer_index] = 0;
            buffer_index += 1;

            // now save the buffer length
            this.m_buffer_length = buffer_index;
        }
    }

    public class CTFTPMessageOutData : CTFTPMessageOut
    {
        // variables
        private ushort m_block_number;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.DATA; }
        }
        public ushort DataLength
        {
            get { return (ushort)(this.m_buffer_length - 4); }
        }

        // functions
        public CTFTPMessageOutData(ushort block_number, byte[] source_bytes, ushort source_byte_count)
        {
            // save passed block number
            this.m_block_number = block_number;

            // allocate the buffer
            this.m_buffer_length = source_byte_count + 4;
            this.m_buffer = new byte[this.m_buffer_length];

            // set the opcode
            ushort opcode = Utilities.htons((ushort)(this.Opcode));
            Array.Copy(BitConverter.GetBytes(opcode), 0, this.m_buffer, 0, 2);

            // set the block number
            block_number = Utilities.htons(block_number);
            Array.Copy(BitConverter.GetBytes(block_number), 0, this.m_buffer, 2, 2);

            // set the number of bytes
            Array.Copy(source_bytes, 0, this.m_buffer, 4, source_byte_count);
        }
    }

    public class CTFTPMessageOutAck : CTFTPMessageOut
    {
        // variables
        private ushort m_block_number;
        private uint m_past_acks;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.ACK; }
        }

        // functions
        public CTFTPMessageOutAck(ushort block_number, uint past_acks)
        {
            // save passed
            this.m_block_number = block_number;
            this.m_past_acks = past_acks;


            // allocate buffer
            this.m_buffer_length = 8;
            this.m_buffer = new byte[this.m_buffer_length];

            // set the opcode
            ushort opcode = Utilities.htons((ushort)(this.Opcode));
            Array.Copy(BitConverter.GetBytes(opcode), 0, this.m_buffer, 0, 2);

            // set block number
            block_number = Utilities.htons((ushort)(block_number));
            Array.Copy(BitConverter.GetBytes(block_number), 0, this.m_buffer, 2, 2);

            // set the past acks...if the received doesnt support out of order then it will ignore it
            Array.Copy(BitConverter.GetBytes(past_acks), 0, this.m_buffer, 4, 4);
        }
    }

    public class CTFTPMessageOutError : CTFTPMessageOut
    {
        // variables
        private EErrorCode m_error_code;
        private string m_error_string;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.ERROR; }
        }

        // functions
        public CTFTPMessageOutError(EErrorCode error_code, string error_string)
        {
            // save passed
            this.m_error_code = error_code;
            this.m_error_string = error_string;

            // allocate the buffer
            this.m_buffer_length = (2 + 2 + error_string.Length + 2);
            this.m_buffer = new byte[this.m_buffer_length];

            // set the opcode
            ushort opcode = Utilities.htons((ushort)(this.Opcode));
            Array.Copy(BitConverter.GetBytes(opcode), 0, this.m_buffer, 0, 2);

            // set the error_code
            ushort ec = Utilities.htons((ushort)(error_code));
            Array.Copy(BitConverter.GetBytes(ec), 0, this.m_buffer, 2, 2);

            // set the error string
            Array.Copy(Encoding.ASCII.GetBytes(error_string), 0, this.m_buffer, 4, error_string.Length);
            // terminate it with a 0
            this.m_buffer[2 + 2 + error_string.Length + 1] = 0;
        }
    }

    public class CTFTPMessageOutOptionAck : CTFTPMessageOut
    {
        // variables
        private Dictionary<string, string> m_options;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.OPTION_ACK; }
        }

        // functions
        public CTFTPMessageOutOptionAck()
        {
            this.m_options = new Dictionary<string, string>();

            // allocate the buffer
            // todo fix this to get exact size and prevent buffer overrun
            this.m_buffer = new byte[1024];
            this.m_buffer_length = 0;

            // set the opcode
            ushort opcode = Utilities.htons((ushort)(this.Opcode));
            Array.Copy(BitConverter.GetBytes(opcode), 0, this.m_buffer, 0, 2);
            this.m_buffer_length += 2;

            // cannot encode options/values at this time as none have been added
            // they are added via add_option(option, value) member function
        }

        public void add_option(string option, string value)
        {
            // put it in our dictionary
            this.m_options[option] = value;

            // put the option into the buffer
            Array.Copy(Encoding.ASCII.GetBytes(option), 0, this.m_buffer, this.m_buffer_length, option.Length);
            this.m_buffer_length += option.Length;
            // 0 terminate
            this.m_buffer[this.m_buffer_length] = 0;
            ++this.m_buffer_length;

            // put the value into the buffer
            Array.Copy(Encoding.ASCII.GetBytes(value), 0, this.m_buffer, this.m_buffer_length, value.Length);
            this.m_buffer_length += value.Length;
            // 0 terminate
            this.m_buffer[this.m_buffer_length] = 0;
            ++this.m_buffer_length;
        }
    }

    // only for our custom server/client to measure RTT
    public class CTFTPMessageOutPing : CTFTPMessageOut
    {
        // variables
        private int m_ping_id;
        private float m_current_time;


        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.PING; }
        }
        public int PingId
        {
            get { return this.m_ping_id; }
        }
        public float Time
        {
            get { return this.m_current_time; }
        }

        // functions
        public CTFTPMessageOutPing(float time, int id)
        {
            // save passed
            this.m_current_time = time;
            this.m_ping_id = id;

            // allocate buffer
            this.m_buffer_length = 10;
            this.m_buffer = new byte[this.m_buffer_length];

            // set the opcode
            ushort opcode = Utilities.htons((ushort)(this.Opcode));
            Array.Copy(BitConverter.GetBytes(opcode), 0, this.m_buffer, 0, 2);

            // set time
            Array.Copy(BitConverter.GetBytes(this.m_current_time), 0, this.m_buffer, 2, 4);

            // set id
            id = Utilities.htoni(id);
            Array.Copy(BitConverter.GetBytes(id), 0, this.m_buffer, 6, 4);
        }
    }
    public class CTFTPMessageOutPong : CTFTPMessageOut
    {
        // variables
        private int m_ping_id;
        private float m_current_time;


        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.PONG; }
        }
        public int PingId
        {
            get { return this.m_ping_id; }
        }
        public float Time
        {
            get { return this.m_current_time; }
        }

        // functions
        public CTFTPMessageOutPong(float time, int id)
        {
            // save passed
            this.m_current_time = time;
            this.m_ping_id = id;

            // allocate buffer
            this.m_buffer_length = 10;
            this.m_buffer = new byte[this.m_buffer_length];

            // set the opcode
            ushort opcode = Utilities.htons((ushort)(this.Opcode));
            Array.Copy(BitConverter.GetBytes(opcode), 0, this.m_buffer, 0, 2);

            // set time
            Array.Copy(BitConverter.GetBytes(this.m_current_time), 0, this.m_buffer, 2, 4);

            // set id
            id = Utilities.htoni(id);
            Array.Copy(BitConverter.GetBytes(id), 0, this.m_buffer, 6, 4);
        }
    }


    public abstract class CTFTPMessageIn
    {
        // variables

        // properties
        public abstract EOpcode Opcode
        {
            get;
        }

        // functions
        public CTFTPMessageIn()
        {

        }
    }

    public class CTFTPMessageInReadRequest : CTFTPMessageIn
    {
        // variables
        private Dictionary<string, string> m_options;
        private string m_filename;
        private ETransferMode m_mode;
        private ushort m_block_size;
        private uint m_total_size;
        private ushort m_timeout_in_secs;
        private ushort m_window_size;
        private bool m_out_of_order;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.READ_REQUEST; }
        }
        public string Filename
        {
            get { return this.m_filename; }
        }
        public ETransferMode TransferMode
        {
            get { return this.m_mode; }
        }
        public ushort BlockSize
        {
            get { return this.m_block_size; }
        }
        public uint TotalSize
        {
            get { return this.m_total_size; }
        }
        public ushort TimeoutInSecs
        {
            get { return this.m_timeout_in_secs; }
        }
        public ushort WindowSize
        {
            get { return this.m_window_size; }
        }
        public bool OutOfOrder
        {
            get { return this.m_out_of_order; }
        }
        public Dictionary<string, string> Options
        {
            get { return this.m_options; }
        }

        // functions
        public CTFTPMessageInReadRequest(byte[] buffer, int buffer_length)
        {
            // init vars
            this.m_options = new Dictionary<string, string>();
            this.m_filename = "";
            this.m_mode = ETransferMode.UNDEFINED;
            this.m_block_size = 512;
            this.m_total_size = 0;
            this.m_timeout_in_secs = 5;

            // index into the buffer array
            int buffer_index = 0;

            // opcode has already been extracted in main receive function which is how we got here
            // in the first place, no need to duplicate effor
            buffer_index += 2;

            // get the filename
            while (buffer[buffer_index] != 0)
            {
                this.m_filename += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                ++buffer_index;
            }
            // get past the terminating 0
            ++buffer_index;

            // get the mode
            string mode = "";
            while (buffer[buffer_index] != 0)
            {
                mode += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                ++buffer_index;
            }
            // get past the terminating 0
            ++buffer_index;
            this.m_mode = Utilities.string_to_tftp_mode(mode);

            // start pulling out options
            while (buffer_index < buffer_length)
            {
                // extract option string
                string option = "";
                while (buffer[buffer_index] != 0)
                {
                    option += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                    ++buffer_index;
                }
                // get past the terminating 0
                ++buffer_index;

                // get the value
                string value = "";
                while (buffer[buffer_index] != 0)
                {
                    value += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                    ++buffer_index;
                }
                // get past the terminating 0
                ++buffer_index;

                // save the options
                this.m_options[option] = value;

                // check which option
                switch (option)
                {
                    case "blksize":
                        {
                            this.m_block_size = Convert.ToUInt16(value);
                        }
                        break;
                    case "tsize":
                        {
                            this.m_total_size = Convert.ToUInt32(value);
                        }
                        break;
                    case "timeout":
                        {
                            this.m_timeout_in_secs = Convert.ToUInt16(value);
                        }
                        break;
                    case "windowsize":
                        {
                            this.m_window_size = Convert.ToUInt16(value);
                        }
                        break;
                    case "outoforder":
                        {
                            if (value == "1")
                            {
                                this.m_out_of_order = true;
                            }
                            else
                            {
                                this.m_out_of_order = false;
                            }
                        }
                        break;
                }
            }
        }
    }

    public class CTFTPMessageInWriteRequest : CTFTPMessageIn
    {
        // variables
        private Dictionary<string, string> m_options;
        private string m_filename;
        private ETransferMode m_mode;
        private ushort m_block_size;
        private uint m_total_size;
        private ushort m_timeout_in_secs;
        private ushort m_window_size;
        private bool m_out_of_order;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.WRITE_REQUEST; }
        }
        public string Filename
        {
            get { return this.m_filename; }
        }
        public ETransferMode TransferMode
        {
            get { return this.m_mode; }
        }
        public ushort BlockSize
        {
            get { return this.m_block_size; }
        }
        public uint TotalSize
        {
            get { return this.m_total_size; }
        }
        public ushort TimeoutInSecs
        {
            get { return this.m_timeout_in_secs; }
        }
        public ushort WindowSize
        {
            get { return this.m_window_size; }
        }
        public bool OutOfOrder
        {
            get { return this.m_out_of_order; }
        }
        public Dictionary<string,string> Options
        {
            get { return this.m_options; }
        }


        // functions
        public CTFTPMessageInWriteRequest(byte[] buffer, int buffer_length)
        {
            // init vars
            this.m_options = new Dictionary<string, string>();
            this.m_filename = "";
            this.m_mode = ETransferMode.UNDEFINED;
            this.m_block_size = 512;
            this.m_total_size = 0;
            this.m_timeout_in_secs = 5;

            // index into the buffer array and temp string for working
            int buffer_index = 0;
            string temp_string = "";


            // opcode has already been extracted in main receive function which is how we got here
            // in the first place, no need to duplicate effor
            buffer_index += 2;

            // get the filename
            while (buffer[buffer_index] != 0)
            {
                this.m_filename += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                ++buffer_index;
            }
            // get past the terminating 0
            ++buffer_index;

            // get the mode
            while (buffer[buffer_index] != 0)
            {
                temp_string += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                ++buffer_index;
            }
            // get past the terminating 0
            ++buffer_index;
            this.m_mode = Utilities.string_to_tftp_mode(temp_string);

            // start pulling out options
            while (buffer_index < buffer_length)
            {
                // extract option string
                string option = "";
                while (buffer[buffer_index] != 0)
                {
                    option += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                    ++buffer_index;
                }
                // get past the terminating 0
                ++buffer_index;

                // get the value
                string value = "";
                while (buffer[buffer_index] != 0)
                {
                    value += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                    ++buffer_index;
                }
                // get past the terminating 0
                ++buffer_index;

                // save the options
                this.m_options[option] = value;

                // check which option
                switch (option)
                {
                    case "blksize":
                        {
                            this.m_block_size = Convert.ToUInt16(value);
                        }
                        break;
                    case "tsize":
                        {
                            this.m_total_size = Convert.ToUInt32(value);
                        }
                        break;
                    case "timeout":
                        {
                            this.m_timeout_in_secs = Convert.ToUInt16(value);
                        }
                        break;
                    case "windowsize":
                        {
                            this.m_window_size = Convert.ToUInt16(value);
                        }
                        break;
                    case "outoforder":
                        {
                            if (value == "1")
                            {
                                this.m_out_of_order = true;
                            }
                            else
                            {
                                this.m_out_of_order = false;
                            }
                        }
                        break;
                }
            }
        }
    }

    public class CTFTPMessageInData : CTFTPMessageIn
    {
        // variables
        private ushort m_block_number;
        private byte[] m_data;
        private ushort m_data_length;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.DATA; }
        }
        public ushort BlockNumber
        {
            get { return this.m_block_number; }
        }
        public byte[] Data
        {
            get { return this.m_data; }
        }
        public ushort DataLength
        {
            get { return this.m_data_length; }
        }

        // functions
        public CTFTPMessageInData(byte[] buffer, int buffer_length)
        {
            // no need to get opcode as it was already decoded to get to this function

            // extract block number
            this.m_block_number = BitConverter.ToUInt16(buffer, 2);
            this.m_block_number = Utilities.ntohs(this.m_block_number);

            // extract data
            this.m_data_length = (ushort)(buffer_length - 4);
            this.m_data = new byte[this.m_data_length];
            Array.Copy(buffer, 4, this.m_data, 0, this.m_data_length);
        }
    }

    public class CTFTPMessageInAck : CTFTPMessageIn
    {
        // variables
        private ushort m_block_number;
        private uint m_past_acks;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.ACK; }
        }
        public ushort BlockNumber
        {
            get { return this.m_block_number; }
        }
        public uint PastAcks
        {
            get { return this.m_past_acks; }
        }

        // functions
        public CTFTPMessageInAck(byte[] buffer, int buffer_length)
        {
            // no need to get opcode as it was already decoded to get to this function

            // extract block number
            this.m_block_number = BitConverter.ToUInt16(buffer, 2);
            this.m_block_number = Utilities.ntohs(this.m_block_number);

            // check for past acks
            if (buffer_length > 4)
            {
                this.m_past_acks = BitConverter.ToUInt32(buffer, 4);
            }
        }
    }

    public class CTFTPMessageInError : CTFTPMessageIn
    {
        // variables
        private EErrorCode m_error_code;
        private string m_error_string;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.ERROR; }
        }
        public EErrorCode ErrorCode
        {
            get { return this.m_error_code; }
        }
        public string ErrorString
        {
            get { return this.m_error_string; }
        }

        // functions
        public CTFTPMessageInError(byte[] buffer, int buffer_length)
        {
            // no need to get opcode as it was already decoded to get to this function
            // so also skip the buffer past it
            int buffer_index = 2;

            // extract error code
            ushort error_code = BitConverter.ToUInt16(buffer, 2);
            error_code = Utilities.ntohs(error_code);
            this.m_error_code = (EErrorCode)(error_code);
            buffer_index += 2;

            // extract error string
            this.m_error_string = "";
            while (buffer[buffer_index] != 0)
            {
                this.m_error_string += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                ++buffer_index;
            }
        }
    }

    public class CTFTPMessageInOptionAck : CTFTPMessageIn
    {
        // variables
        private Dictionary<string, string> m_options;
        private ushort m_block_size;
        private uint m_total_size;
        private ushort m_timeout_in_secs;
        private ushort m_window_size;
        private bool m_out_of_order;

        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.OPTION_ACK; }
        }
        public ushort BlockSize
        {
            get { return this.m_block_size; }
        }
        public uint TotalSize
        {
            get { return this.m_total_size; }
        }
        public ushort TimeoutInSecs
        {
            get { return this.m_timeout_in_secs; }
        }
        public ushort WindowSize
        {
            get { return this.m_window_size; }
        }
        public bool OutOfOrder
        {
            get { return this.m_out_of_order; }
        }
        public Dictionary<string, string> Options
        {
            get { return this.m_options; }
        }

        // functions
        public CTFTPMessageInOptionAck(byte[] buffer, int buffer_length)
        {
            this.m_options = new Dictionary<string, string>();

            // initial values for the options
            this.m_block_size = 512;
            this.m_total_size = 0;
            this.m_timeout_in_secs = 3;
            this.m_window_size = 1;
            this.m_out_of_order = false;

            // no need to get opcode as it was already decoded to get to this function
            // so also skip the buffer past it
            int buffer_index = 2;

            // get options
            while (buffer_index < buffer_length)
            {
                // extract option string
                string option = "";
                while (buffer[buffer_index] != 0)
                {
                    option += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                    ++buffer_index;
                }
                // get past the terminating 0
                ++buffer_index;

                // get the value
                string value = "";
                while (buffer[buffer_index] != 0)
                {
                    value += Encoding.ASCII.GetString(buffer, buffer_index, 1);
                    ++buffer_index;
                }
                // get past the terminating 0
                ++buffer_index;

                // put it in our map
                this.m_options[option] = value;

                // check for the standard
                switch (option)
                {
                    case "blksize":
                        {
                            this.m_block_size = Convert.ToUInt16(value);
                        } break;
                    case "tsize":
                        {
                            this.m_total_size = Convert.ToUInt32(value);
                        } break;
                    case "timeout":
                        {
                            this.m_timeout_in_secs = Convert.ToUInt16(value);
                        }
                        break;
                    case "windowsize":
                        {
                            this.m_window_size = Convert.ToUInt16(value);
                        }
                        break;
                    case "outoforder":
                        {
                            if (value == "1")
                            {
                                this.m_out_of_order = true;
                            }
                            else
                            {
                                this.m_out_of_order = false;
                            }
                        }
                        break;
                }
            }
        }

        public string get_option_value(string option)
        {
            if (this.m_options.ContainsKey(option))
            {
                return this.m_options[option];
            }
            return "";
        }
    }

    // only for our custom server/client to measure RTT
    public class CTFTPMessageInPing : CTFTPMessageIn
    {
        // variables
        private int m_ping_id;
        private float m_current_time;


        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.PING; }
        }
        public int PingId
        {
            get { return this.m_ping_id; }
        }
        public float Time
        {
            get { return this.m_current_time; }
        }

        // functions
        public CTFTPMessageInPing(byte[] buffer, int buffer_length)
        {
            // no need to get opcode as it was already decoded to get to this function

            // extract time
            this.m_current_time = BitConverter.ToSingle(buffer, 2);

            // extract id
            this.m_ping_id = BitConverter.ToInt32(buffer, 6);
            this.m_ping_id = Utilities.ntohi(this.m_ping_id);
        }
    }
    public class CTFTPMessageInPong : CTFTPMessageIn
    {
        // variables
        private int m_ping_id;
        private float m_current_time;


        // properties
        public override EOpcode Opcode
        {
            get { return EOpcode.PONG; }
        }
        public int PingId
        {
            get { return this.m_ping_id; }
        }
        public float Time
        {
            get { return this.m_current_time; }
        }

        // functions
        public CTFTPMessageInPong(byte[] buffer, int buffer_length)
        {
            // no need to get opcode as it was already decoded to get to this function

            // extract time
            this.m_current_time = BitConverter.ToSingle(buffer, 2);

            // extract id
            this.m_ping_id = BitConverter.ToInt32(buffer, 6);
            this.m_ping_id = Utilities.ntohi(this.m_ping_id);
        }
    }
}
