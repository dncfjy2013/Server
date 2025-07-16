using Entity.Communication.ComComu.Common;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu
{
    // 字符串协议实现
    public class StringProtocol : IProtocol
    {
        public string Name => "String";

        private readonly Encoding _encoding;
        private readonly char _delimiter;
        private readonly bool _includeDelimiter;

        public StringProtocol(Encoding encoding = null, char delimiter = '\n', bool includeDelimiter = false)
        {
            _encoding = encoding ?? Encoding.ASCII;
            _delimiter = delimiter;
            _includeDelimiter = includeDelimiter;
        }

        public byte[] EncodeMessage(byte[] data)
        {
            // 如果已经是字符串编码的字节数组，直接添加分隔符
            if (_includeDelimiter)
            {
                byte[] encoded = new byte[data.Length + 1];
                Array.Copy(data, 0, encoded, 0, data.Length);
                encoded[data.Length] = (byte)_delimiter;
                return encoded;
            }

            return data;
        }

        public byte[] DecodeMessage(byte[] data)
        {
            // 如果包含分隔符，移除它
            if (_includeDelimiter && data.Length > 0 && data[data.Length - 1] == (byte)_delimiter)
            {
                byte[] decoded = new byte[data.Length - 1];
                Array.Copy(data, 0, decoded, 0, decoded.Length);
                return decoded;
            }

            return data;
        }

        public bool ValidateMessage(byte[] data)
        {
            // 只要有数据就算有效消息
            return data != null && data.Length > 0;
        }

        public int GetMessageLength(byte[] buffer)
        {
            // 查找分隔符
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == (byte)_delimiter)
                    return i + 1;
            }

            return -1;
        }

        public Encoding GetEncoding()
        {
            return _encoding;
        }
    }

    // 字符串通信类
    public class StringSerialCommunicator : SerialComuBase
    {
        public StringSerialCommunicator(string portName, int baudRate = 9600,
            Parity parity = Parity.None, int dataBits = 8,
            StopBits stopBits = StopBits.One, Encoding encoding = null, char delimiter = '\n')
            : base(portName, baudRate, parity, dataBits, stopBits)
        {
            // 设置字符串协议
            Protocol = new StringProtocol(encoding, delimiter);
        }

        // 发送字符串消息
        public void SendString(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            byte[] data = ((StringProtocol)Protocol).GetEncoding().GetBytes(message);
            Send(data);
        }

        // 接收字符串消息事件
        public event EventHandler<string> StringMessageReceived;

        protected override void ProcessReceivedData(byte[] data)
        {
            try
            {
                // 解码为字符串
                string message = ((StringProtocol)Protocol).GetEncoding().GetString(data);

                // 触发字符串消息接收事件
                StringMessageReceived?.Invoke(this, message);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.ProtocolError, $"解析字符串消息失败: {ex.Message}", ex);
            }

            // 调用基类处理
            base.ProcessReceivedData(data);
        }
    }

    // 使用示例
    public class StringProtocolExample
    {
        public static void Main(string[] args)
        {
            // 字符串通信示例 - 使用默认配置
            using (StringSerialCommunicator stringComm = new StringSerialCommunicator("COM1", 9600))
            {
                // 注册字符串消息接收事件
                stringComm.StringMessageReceived += (sender, message) =>
                {
                    Console.WriteLine($"收到字符串消息: {message}");
                };

                stringComm.Open();

                // 发送字符串消息
                stringComm.SendString("Hello, this is a string message!");
                stringComm.SendString("Another line\n"); // 包含分隔符
            }

            // 字符串通信示例 - 使用自定义编码和分隔符
            using (StringSerialCommunicator stringComm = new StringSerialCommunicator(
                "COM1", 9600, Parity.None, 8, StopBits.One,
                Encoding.UTF8, '\r')) // 使用UTF-8编码和回车符作为分隔符
            {
                stringComm.Open();

                // 发送包含中文的UTF-8字符串
                stringComm.SendString("你好，世界！");
            }
        }
    }
}
