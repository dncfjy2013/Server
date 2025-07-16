using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Entity.Common
{
    // PLC通讯异常类，用于封装通讯过程中的异常
    public class PLCCommunicationException : Exception
    {
        public PLCCommunicationException(string message) : base(message) { }
        public PLCCommunicationException(string message, Exception inner) : base(message, inner) { }
    }

    // PLC数据类型枚举
    public enum PLCDataType
    {
        DB,     // 数据块
        Input,  // 输入区
        Output, // 输出区
        M,      // 标志位
        T,      // 定时器
        C       // 计数器
    }

    // PLC通讯类，实现S7协议通讯
    public class S7PLC : IDisposable
    {
        // TCP客户端对象，用于与PLC建立网络连接
        private TcpClient _client;
        // 网络流对象，用于通过TCP连接发送和接收数据
        private NetworkStream _stream;
        // PLC的IP地址，用于建立网络连接
        private readonly string _ipAddress;
        // PLC的端口号，默认值为102（西门子PLC的标准端口）
        private readonly int _port;
        // PLC的机架号(Rack)，用于标识PLC在分布式系统中的物理位置
        private readonly int _rack;
        // PLC的插槽号(Slot)，用于标识CPU模块在机架中的位置
        private readonly int _slot;
        // 网络操作超时时间（毫秒），用于设置连接超时和读写超时
        private readonly int _timeout;
        // PDU(协议数据单元)引用号，用于标识不同的通信请求
        // 每次发送请求时递增，确保请求与响应正确匹配
        private byte _pduReference = 0;
        // 对象处置状态标志，用于实现IDisposable模式
        // 确保资源只被释放一次，防止重复处置
        private bool _disposed = false;

        // 构造函数，初始化PLC连接参数
        public S7PLC(string ipAddress, int port = 102, int rack = 0, int slot = 1, int timeout = 1000)
        {
            _ipAddress = ipAddress;
            _port = port;
            _rack = rack;
            _slot = slot;
            _timeout = timeout;
        }

        // 连接PLC
        public bool Connect()
        {
            try
            {
                // 创建TCP客户端并连接到PLC
                _client = new TcpClient();
                _client.Connect(IPAddress.Parse(_ipAddress), _port);
                _client.ReceiveTimeout = _timeout;
                _client.SendTimeout = _timeout;

                _stream = _client.GetStream();

                // 1. 发送COTP连接请求并接收响应
                SendCOTPConnectRequest();

                // 2. 发送S7通信设置请求并接收响应
                SendSetupCommunicationRequest();

                return true;
            }
            catch (Exception ex)
            {
                Disconnect();
                throw new PLCCommunicationException("PLC连接失败", ex);
            }
        }

        // 断开连接
        public void Disconnect()
        {
            try
            {
                _stream?.Close();
                _client?.Close();
            }
            catch (Exception ex)
            {
                throw new PLCCommunicationException("断开连接错误", ex);
            }
        }

        // 读取PLC数据
        public byte[] Read(PLCDataType dataType, int dbNumber, int startAddress, int length)
        {
            try
            {
                byte[] request = BuildReadRequest(dataType, dbNumber, startAddress, length);
                SendRequest(request);

                byte[] response = ReceiveResponse();
                return ParseReadResponse(response);
            }
            catch (Exception ex)
            {
                throw new PLCCommunicationException($"读取{dataType}失败", ex);
            }
        }

        // 写入PLC数据
        public bool Write(PLCDataType dataType, int dbNumber, int startAddress, byte[] data)
        {
            try
            {
                byte[] request = BuildWriteRequest(dataType, dbNumber, startAddress, data);
                SendRequest(request);

                byte[] response = ReceiveResponse();
                return ParseWriteResponse(response);
            }
            catch (Exception ex)
            {
                throw new PLCCommunicationException($"写入{dataType}失败", ex);
            }
        }

        // 发送COTP连接请求
        private void SendCOTPConnectRequest()
        {
            byte[] request = BuildCOTPConnectRequest();
            SendRequest(request);

            byte[] response = ReceiveResponse();
            ValidateCOTPConnectResponse(response);
        }

        // 发送S7通信设置请求
        private void SendSetupCommunicationRequest()
        {
            byte[] request = BuildSetupCommunicationRequest();
            SendRequest(request);

            byte[] response = ReceiveResponse();
            ValidateSetupCommunicationResponse(response);
        }

        // 构建COTP连接请求报文
        private byte[] BuildCOTPConnectRequest()
        {
            byte[] buffer = new byte[256];
            int pos = 0;

            // TPKT Header (4 bytes)
            buffer[pos++] = 0x03;  // Version
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // Length high
            buffer[pos++] = 0x16;  // Length low (22 bytes)

            // COTP Header (7 bytes)
            buffer[pos++] = 0x02;  // Length
            buffer[pos++] = 0xF0;  // COTP TPDU Type: CR (Connection Request)
            buffer[pos++] = 0x80;  // Destination Reference High
            buffer[pos++] = 0x00;  // Destination Reference Low
            buffer[pos++] = 0x00;  // Source Reference High
            buffer[pos++] = 0x00;  // Source Reference Low
            buffer[pos++] = 0xE0;  // Class/Options

            // Parameters - Destination TSAP (4 bytes)
            buffer[pos++] = 0xC0;  // Parameter Code: Destination TSAP
            buffer[pos++] = 0x02;  // Parameter Length
            buffer[pos++] = (byte)(0x10 + _rack);  // TSAP High Byte (Rack)
            buffer[pos++] = (byte)_slot;  // TSAP Low Byte (Slot)

            // Parameters - Source TSAP (4 bytes)
            buffer[pos++] = 0xC1;  // Parameter Code: Source TSAP
            buffer[pos++] = 0x02;  // Parameter Length
            buffer[pos++] = 0x01;  // TSAP High Byte
            buffer[pos++] = 0x00;  // TSAP Low Byte

            // Parameters - Protocol Class (3 bytes)
            buffer[pos++] = 0xC2;  // Parameter Code: Protocol Class
            buffer[pos++] = 0x01;  // Parameter Length
            buffer[pos++] = 0x00;  // Protocol Class 0

            return buffer.Take(pos).ToArray();
        }

        // 构建S7通信设置请求报文
        private byte[] BuildSetupCommunicationRequest()
        {
            byte[] buffer = new byte[256];
            int pos = 0;

            // TPKT Header (4 bytes)
            buffer[pos++] = 0x03;  // Version
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // Length high
            buffer[pos++] = 0x19;  // Length low (25 bytes)

            // COTP Header (3 bytes)
            buffer[pos++] = 0x02;  // Length
            buffer[pos++] = 0xF0;  // COTP TPDU Type: DT (Data Transfer)
            buffer[pos++] = 0x80;  // EOT and sequence number

            // S7 Header (10 bytes)
            buffer[pos++] = 0x32;  // Protocol ID
            buffer[pos++] = 0x01;  // Message Type: Job
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // PDU Reference High
            buffer[pos++] = _pduReference++;  // PDU Reference Low
            buffer[pos++] = 0x00;  // Parameter Length High
            buffer[pos++] = 0x0A;  // Parameter Length Low (10 bytes)
            buffer[pos++] = 0x00;  // Data Length High
            buffer[pos++] = 0x00;  // Data Length Low

            // Parameters (8 bytes)
            buffer[pos++] = 0xF0;  // Function Code: Setup Communication
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // Max AMQ Caller High
            buffer[pos++] = 0x01;  // Max AMQ Caller Low
            buffer[pos++] = 0x00;  // Max AMQ Callee High
            buffer[pos++] = 0x01;  // Max AMQ Callee Low
            buffer[pos++] = 0x04;  // PDU Length High (1024 bytes)
            buffer[pos++] = 0x00;  // PDU Length Low

            return buffer.Take(pos).ToArray();
        }

        // 构建读取请求报文
        private byte[] BuildReadRequest(PLCDataType dataType, int dbNumber, int startAddress, int length)
        {
            byte[] buffer = new byte[256];
            int pos = 0;

            // TPKT Header (4 bytes)
            buffer[pos++] = 0x03;  // Version
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // Length high (will be calculated later)
            buffer[pos++] = 0x00;  // Length low

            // COTP Header (3 bytes)
            buffer[pos++] = 0x02;  // Length
            buffer[pos++] = 0xF0;  // COTP TPDU Type: DT (Data Transfer)
            buffer[pos++] = 0x80;  // EOT and sequence number

            // S7 Header (10 bytes)
            buffer[pos++] = 0x32;  // Protocol ID
            buffer[pos++] = 0x01;  // Message Type: Job
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // PDU Reference High
            buffer[pos++] = _pduReference++;  // PDU Reference Low
            buffer[pos++] = 0x00;  // Parameter Length High (will be calculated later)
            buffer[pos++] = 0x00;  // Parameter Length Low
            buffer[pos++] = 0x00;  // Data Length High
            buffer[pos++] = 0x00;  // Data Length Low

            int parameterStartPos = pos;

            // Parameters
            buffer[pos++] = 0x04;  // Function Code: Read Var
            buffer[pos++] = 0x01;  // Item Count

            // 读取项规范
            buffer[pos++] = 0x12;  // Syntax ID: S7ANY
            buffer[pos++] = 0x0A;  // Variable Specification Length
            buffer[pos++] = GetAreaCode(dataType);  // Area

            // DB Number (if applicable)
            if (dataType == PLCDataType.DB)
            {
                buffer[pos++] = (byte)(dbNumber >> 8);  // DB Number High
                buffer[pos++] = (byte)(dbNumber & 0xFF);  // DB Number Low
            }

            // Start Address (3 bytes)
            buffer[pos++] = 0x00;  // Address High Byte
            buffer[pos++] = (byte)((startAddress >> 16) & 0xFF);  // Address Middle Byte
            buffer[pos++] = (byte)((startAddress >> 8) & 0xFF);  // Address Low Byte 1
            buffer[pos++] = (byte)(startAddress & 0xFF);  // Address Low Byte 2

            // Number of items
            buffer[pos++] = 0x00;  // Transport Size High
            buffer[pos++] = 0x04;  // Transport Size Low (1 = bit, 2 = byte, 4 = word, 6 = double word)
            buffer[pos++] = (byte)(length >> 8);  // Number of items High
            buffer[pos++] = (byte)(length & 0xFF);  // Number of items Low

            int parameterLength = pos - parameterStartPos;
            buffer[14] = (byte)(parameterLength >> 8);  // Parameter Length High
            buffer[15] = (byte)(parameterLength & 0xFF);  // Parameter Length Low

            int totalLength = pos - 2;  // Subtract TPKT header size
            buffer[2] = (byte)(totalLength >> 8);  // Length high
            buffer[3] = (byte)(totalLength & 0xFF);  // Length low

            return buffer.Take(pos).ToArray();
        }

        // 构建写入请求报文
        private byte[] BuildWriteRequest(PLCDataType dataType, int dbNumber, int startAddress, byte[] data)
        {
            byte[] buffer = new byte[512];
            int pos = 0;

            // TPKT Header (4 bytes)
            buffer[pos++] = 0x03;  // Version
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // Length high (will be calculated later)
            buffer[pos++] = 0x00;  // Length low

            // COTP Header (3 bytes)
            buffer[pos++] = 0x02;  // Length
            buffer[pos++] = 0xF0;  // COTP TPDU Type: DT (Data Transfer)
            buffer[pos++] = 0x80;  // EOT and sequence number

            // S7 Header (10 bytes)
            buffer[pos++] = 0x32;  // Protocol ID
            buffer[pos++] = 0x01;  // Message Type: Job
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // Reserved
            buffer[pos++] = 0x00;  // PDU Reference High
            buffer[pos++] = _pduReference++;  // PDU Reference Low
            buffer[pos++] = 0x00;  // Parameter Length High (will be calculated later)
            buffer[pos++] = 0x00;  // Parameter Length Low
            buffer[pos++] = 0x00;  // Data Length High (will be calculated later)
            buffer[pos++] = 0x00;  // Data Length Low

            int parameterStartPos = pos;

            // Parameters
            buffer[pos++] = 0x05;  // Function Code: Write Var
            buffer[pos++] = 0x01;  // Item Count

            // 写入项规范
            buffer[pos++] = 0x12;  // Syntax ID: S7ANY
            buffer[pos++] = 0x0A;  // Variable Specification Length
            buffer[pos++] = GetAreaCode(dataType);  // Area

            // DB Number (if applicable)
            if (dataType == PLCDataType.DB)
            {
                buffer[pos++] = (byte)(dbNumber >> 8);  // DB Number High
                buffer[pos++] = (byte)(dbNumber & 0xFF);  // DB Number Low
            }

            // Start Address (3 bytes)
            buffer[pos++] = 0x00;  // Address High Byte
            buffer[pos++] = (byte)((startAddress >> 16) & 0xFF);  // Address Middle Byte
            buffer[pos++] = (byte)((startAddress >> 8) & 0xFF);  // Address Low Byte 1
            buffer[pos++] = (byte)(startAddress & 0xFF);  // Address Low Byte 2

            // Number of items
            buffer[pos++] = 0x00;  // Transport Size High
            buffer[pos++] = 0x04;  // Transport Size Low (1 = bit, 2 = byte, 4 = word, 6 = double word)
            buffer[pos++] = (byte)(data.Length >> 8);  // Number of items High
            buffer[pos++] = (byte)(data.Length & 0xFF);  // Number of items Low

            int parameterLength = pos - parameterStartPos;
            buffer[14] = (byte)(parameterLength >> 8);  // Parameter Length High
            buffer[15] = (byte)(parameterLength & 0xFF);  // Parameter Length Low

            int dataStartPos = pos;

            // Data
            buffer[pos++] = 0x00;  // Return Code
            buffer[pos++] = 0x04;  // Transport Size (4 = byte)
            buffer[pos++] = (byte)(data.Length >> 8);  // Data Length High
            buffer[pos++] = (byte)(data.Length & 0xFF);  // Data Length Low

            // Copy data
            Array.Copy(data, 0, buffer, pos, data.Length);
            pos += data.Length;

            int dataLength = pos - dataStartPos;
            buffer[16] = (byte)(dataLength >> 8);  // Data Length High
            buffer[17] = (byte)(dataLength & 0xFF);  // Data Length Low

            int totalLength = pos - 2;  // Subtract TPKT header size
            buffer[2] = (byte)(totalLength >> 8);  // Length high
            buffer[3] = (byte)(totalLength & 0xFF);  // Length low

            return buffer.Take(pos).ToArray();
        }

        // 获取区域代码
        private byte GetAreaCode(PLCDataType dataType)
        {
            switch (dataType)
            {
                case PLCDataType.DB: return 0x84;
                case PLCDataType.Input: return 0x81;
                case PLCDataType.Output: return 0x82;
                case PLCDataType.M: return 0x83;
                case PLCDataType.T: return 0x1C;
                case PLCDataType.C: return 0x1D;
                default: throw new ArgumentException("不支持的PLC数据类型");
            }
        }

        // 发送请求
        private void SendRequest(byte[] request)
        {
            if (_stream == null || !_client.Connected)
                throw new PLCCommunicationException("未连接到PLC");

            _stream.Write(request, 0, request.Length);
        }

        // 接收响应
        private byte[] ReceiveResponse()
        {
            if (_stream == null || !_client.Connected)
                throw new PLCCommunicationException("未连接到PLC");

            byte[] header = new byte[4];
            int bytesRead = _stream.Read(header, 0, 4);

            if (bytesRead < 4)
                throw new PLCCommunicationException("接收响应超时或不完整");

            int messageLength = (header[2] << 8) | header[3];
            byte[] response = new byte[messageLength + 2];

            Array.Copy(header, 0, response, 0, 4);
            int remainingBytes = messageLength - 2;

            while (remainingBytes > 0)
            {
                bytesRead = _stream.Read(response, messageLength + 2 - remainingBytes, remainingBytes);
                if (bytesRead <= 0)
                    throw new PLCCommunicationException("接收响应超时或不完整");

                remainingBytes -= bytesRead;
            }

            return response;
        }

        // 验证COTP连接响应
        private void ValidateCOTPConnectResponse(byte[] response)
        {
            // 检查响应是否包含有效COTP连接确认
            if (response.Length < 7 || response[5] != 0xD0)  // 0xD0 = CC (Connection Confirm)
                throw new PLCCommunicationException("COTP连接失败");
        }

        // 验证通信设置响应
        private void ValidateSetupCommunicationResponse(byte[] response)
        {
            // 检查响应是否包含有效通信设置确认
            if (response.Length < 12 || response[9] != 0x03)  // 0x03 = Ack_Data
                throw new PLCCommunicationException("通信设置失败");
        }

        // 解析读取响应
        private byte[] ParseReadResponse(byte[] response)
        {
            try
            {
                // 检查基本响应格式
                if (response.Length < 20 || response[8] != 0x03)  // 0x03 = Ack_Data
                    throw new PLCCommunicationException("读取响应格式错误");

                // 检查返回码
                int returnCodePos = 20;  // 通常返回码位置
                if (response[returnCodePos] != 0x00)  // 0x00 = Success
                    throw new PLCCommunicationException($"读取失败，错误码: 0x{response[returnCodePos]:X2}");

                // 提取数据
                int dataLength = (response[returnCodePos + 2] << 8) | response[returnCodePos + 3];
                byte[] data = new byte[dataLength];

                Array.Copy(response, returnCodePos + 4, data, 0, dataLength);
                return data;
            }
            catch (Exception ex)
            {
                throw new PLCCommunicationException("解析读取响应失败", ex);
            }
        }

        // 解析写入响应
        private bool ParseWriteResponse(byte[] response)
        {
            try
            {
                // 检查基本响应格式
                if (response.Length < 20 || response[8] != 0x03)  // 0x03 = Ack_Data
                    throw new PLCCommunicationException("写入响应格式错误");

                // 检查返回码
                int returnCodePos = 20;  // 通常返回码位置
                return response[returnCodePos] == 0x00;  // 0x00 = Success
            }
            catch (Exception ex)
            {
                throw new PLCCommunicationException("解析写入响应失败", ex);
            }
        }

        // 实现IDisposable接口
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    Disconnect();
                }

                // 释放非托管资源

                _disposed = true;
            }
        }

        ~S7PLC()
        {
            Dispose(false);
        }
    }

    // 扩展方法类，提供便捷的数据转换方法
    public static class PLCDataExtensions
    {
        // 从字节数组中读取布尔值
        public static bool GetBoolean(this byte[] data, int byteIndex, int bitIndex)
        {
            if (byteIndex < 0 || byteIndex >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(byteIndex));

            if (bitIndex < 0 || bitIndex > 7)
                throw new ArgumentOutOfRangeException(nameof(bitIndex));

            return (data[byteIndex] & (1 << bitIndex)) != 0;
        }

        // 从字节数组中读取字节值
        public static byte GetByte(this byte[] data, int index)
        {
            if (index < 0 || index >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return data[index];
        }

        // 从字节数组中读取短整型值(2字节，低字节在前)
        public static short GetInt16(this byte[] data, int index)
        {
            if (index < 0 || index + 1 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (short)((data[index + 1] << 8) | data[index]);
        }

        // 从字节数组中读取无符号短整型值(2字节，低字节在前)
        public static ushort GetUInt16(this byte[] data, int index)
        {
            if (index < 0 || index + 1 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (ushort)((data[index + 1] << 8) | data[index]);
        }

        // 从字节数组中读取整型值(4字节，低字节在前)
        public static int GetInt32(this byte[] data, int index)
        {
            if (index < 0 || index + 3 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (data[index + 3] << 24) | (data[index + 2] << 16) | (data[index + 1] << 8) | data[index];
        }

        // 从字节数组中读取无符号整型值(4字节，低字节在前)
        public static uint GetUInt32(this byte[] data, int index)
        {
            if (index < 0 || index + 3 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (uint)((data[index + 3] << 24) | (data[index + 2] << 16) | (data[index + 1] << 8) | data[index]);
        }

        // 从字节数组中读取浮点数(4字节，低字节在前)
        public static float GetFloat(this byte[] data, int index)
        {
            if (index < 0 || index + 3 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            int intValue = (data[index + 3] << 24) | (data[index + 2] << 16) | (data[index + 1] << 8) | data[index];
            return BitConverter.ToSingle(BitConverter.GetBytes(intValue), 0);
        }

        // 从字节数组中读取字符串
        public static string GetString(this byte[] data, int index, int maxLength)
        {
            if (index < 0 || index + maxLength > data.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            int length = data[index];
            if (length > maxLength - 1)
                length = maxLength - 1;

            return Encoding.ASCII.GetString(data, index + 1, length);
        }

        // 将布尔值转换为字节数组
        public static byte[] ToByteArray(this bool value, int byteIndex, int bitIndex)
        {
            byte[] data = new byte[byteIndex + 1];
            if (value)
                data[byteIndex] |= (byte)(1 << bitIndex);
            else
                data[byteIndex] &= (byte)~(1 << bitIndex);

            return data;
        }

        // 将字节值转换为字节数组
        public static byte[] ToByteArray(this byte value)
        {
            return new byte[] { value };
        }

        // 将短整型值转换为字节数组(2字节，低字节在前)
        public static byte[] ToByteArray(this short value)
        {
            return new byte[] { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) };
        }

        // 将无符号短整型值转换为字节数组(2字节，低字节在前)
        public static byte[] ToByteArray(this ushort value)
        {
            return new byte[] { (byte)(value & 0xFF), (byte)((value >> 8) & 0xFF) };
        }

        // 将整型值转换为字节数组(4字节，低字节在前)
        public static byte[] ToByteArray(this int value)
        {
            return new byte[] {
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF)
            };
        }

        // 将无符号整型值转换为字节数组(4字节，低字节在前)
        public static byte[] ToByteArray(this uint value)
        {
            return new byte[] {
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 24) & 0xFF)
            };
        }

        // 将浮点数转换为字节数组(4字节，低字节在前)
        public static byte[] ToByteArray(this float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                return bytes;

            return new byte[] { bytes[3], bytes[2], bytes[1], bytes[0] };
        }

        // 将字符串转换为字节数组
        public static byte[] ToByteArray(this string value, int maxLength)
        {
            if (value == null)
                value = "";

            byte[] data = new byte[maxLength];
            byte[] valueBytes = Encoding.ASCII.GetBytes(value);
            int length = Math.Min(valueBytes.Length, maxLength - 1);

            data[0] = (byte)length;
            Array.Copy(valueBytes, 0, data, 1, length);

            return data;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // 使用示例
                using (S7PLC plc = new S7PLC("192.168.0.1", 102, 0, 1))
                {
                    if (plc.Connect())
                    {
                        Console.WriteLine("PLC连接成功");

                        // 示例1: 读取DB1从0地址开始的10个字节
                        byte[] dbData = plc.Read(PLCDataType.DB, 1, 0, 10);
                        if (dbData != null)
                        {
                            Console.WriteLine("读取DB1成功");

                            // 读取不同类型的数据
                            bool boolValue = dbData.GetBoolean(0, 0);
                            short intValue = dbData.GetInt16(2);
                            float floatValue = dbData.GetFloat(4);

                            Console.WriteLine($"布尔值: {boolValue}");
                            Console.WriteLine($"短整数值: {intValue}");
                            Console.WriteLine($"浮点数值: {floatValue}");
                        }

                        // 示例2: 写入数据到DB1
                        byte[] writeData = 123.45f.ToByteArray();  // 写入浮点数
                        bool writeResult = plc.Write(PLCDataType.DB, 1, 10, writeData);
                        Console.WriteLine($"写入DB1: {writeResult}");

                        // 示例3: 写入布尔值
                        byte[] boolData = true.ToByteArray(0, 7);  // 第0字节的第7位
                        writeResult = plc.Write(PLCDataType.DB, 1, 20, boolData);
                        Console.WriteLine($"写入布尔值: {writeResult}");

                        Console.WriteLine("已断开PLC连接");
                    }
                    else
                    {
                        Console.WriteLine("PLC连接失败");
                    }
                }
            }
            catch (PLCCommunicationException ex)
            {
                Console.WriteLine($"PLC通信错误: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"内部错误: {ex.InnerException.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生未知错误: {ex.Message}");
            }

            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }

}
