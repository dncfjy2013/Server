using Entity.Communication.ComComu.Common;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu
{
    // ASCII协议实现
    public class AsciiProtocol : IProtocol
    {
        public string Name => "ASCII";
        private readonly char _startChar;
        private readonly char _endChar;

        public AsciiProtocol(char startChar = ':', char endChar = '\n')
        {
            _startChar = startChar;
            _endChar = endChar;
        }

        public byte[] EncodeMessage(byte[] data)
        {
            List<byte> encoded = new List<byte>();
            encoded.Add((byte)_startChar);
            encoded.AddRange(data);
            encoded.Add((byte)_endChar);
            return encoded.ToArray();
        }

        public byte[] DecodeMessage(byte[] data)
        {
            if (data.Length < 2)
                return new byte[0];

            return data.Skip(1).Take(data.Length - 2).ToArray();
        }

        public bool ValidateMessage(byte[] data)
        {
            return data.Length >= 2 &&
                   data[0] == (byte)_startChar &&
                   data[data.Length - 1] == (byte)_endChar;
        }

        public int GetMessageLength(byte[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] == (byte)_endChar)
                    return i + 1;
            }
            return -1;
        }
    }

    // ASCII通信类
    public class AsciiSerialCommunicator : SerialComuBase
    {
        public AsciiSerialCommunicator(string portName, int baudRate = 9600,
            Parity parity = Parity.None, int dataBits = 8,
            StopBits stopBits = StopBits.One)
            : base(portName, baudRate, parity, dataBits, stopBits)
        {
            // 设置ASCII协议
            Protocol = new AsciiProtocol();
        }

        // 发送ASCII消息
        public void SendAsciiMessage(string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            Send(data);
        }

        // 接收ASCII消息
        public event EventHandler<string> AsciiMessageReceived;

        protected override void ProcessReceivedData(byte[] data)
        {
            try
            {
                string asciiMessage = Encoding.ASCII.GetString(data);
                AsciiMessageReceived?.Invoke(this, asciiMessage);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.ProtocolError, $"解析ASCII消息失败: {ex.Message}", ex);
            }

            // 调用基类处理
            base.ProcessReceivedData(data);
        }
    }

    // 使用示例
    public class AsciiProtocolExample
    {
        public static void Main(string[] args)
        {
            // ASCII通信示例
            using (AsciiSerialCommunicator asciiComm = new AsciiSerialCommunicator("COM1", 9600))
            {
                asciiComm.AsciiMessageReceived += (sender, message) =>
                {
                    Console.WriteLine($"收到ASCII消息: {message}");
                };

                asciiComm.Open();
                asciiComm.SendAsciiMessage("Hello, ASCII Protocol!\r\n");
            }
        }
    }
}
