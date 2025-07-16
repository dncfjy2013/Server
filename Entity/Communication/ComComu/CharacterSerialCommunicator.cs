using Entity.Communication.ComComu.Common;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu
{
    // 字符协议实现
    public class CharacterProtocol : IProtocol
    {
        public string Name => "Character";

        private readonly Encoding _encoding;
        private readonly char[] _specialCharacters;
        private readonly bool _passSpecialCharacters;

        public CharacterProtocol(Encoding encoding = null, char[] specialCharacters = null, bool passSpecialCharacters = true)
        {
            _encoding = encoding ?? Encoding.ASCII;
            _specialCharacters = specialCharacters ?? Array.Empty<char>();
            _passSpecialCharacters = passSpecialCharacters;
        }

        public byte[] EncodeMessage(byte[] data)
        {
            return data; // 字符协议直接使用原始字节
        }

        public byte[] DecodeMessage(byte[] data)
        {
            return data; // 字符协议直接使用原始字节
        }

        public bool ValidateMessage(byte[] data)
        {
            return data != null && data.Length > 0; // 只要有数据就算有效
        }

        public int GetMessageLength(byte[] buffer)
        {
            return buffer.Length > 0 ? 1 : -1; // 每次处理一个字符
        }

        // 检查是否是特殊字符
        public bool IsSpecialCharacter(char c)
        {
            return _specialCharacters.Contains(c);
        }

        public Encoding GetEncoding()
        {
            return _encoding;
        }
    }

    // 字符通信类
    public class CharacterSerialCommunicator : SerialComuBase
    {
        private readonly CharacterProtocol _characterProtocol;

        public CharacterSerialCommunicator(string portName, int baudRate = 9600,
            Parity parity = Parity.None, int dataBits = 8,
            StopBits stopBits = StopBits.One, Encoding encoding = null,
            char[] specialCharacters = null, bool passSpecialCharacters = true)
            : base(portName, baudRate, parity, dataBits, stopBits)
        {
            // 创建并设置字符协议
            _characterProtocol = new CharacterProtocol(encoding, specialCharacters, passSpecialCharacters);
            Protocol = _characterProtocol;
        }

        // 发送单个字符
        public void SendCharacter(char c)
        {
            byte[] data = _characterProtocol.GetEncoding().GetBytes(new[] { c });
            Send(data);
        }

        // 发送字符串（按字符发送）
        public void SendStringAsCharacters(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            foreach (char c in text)
            {
                SendCharacter(c);
            }
        }

        // 字符接收事件
        public event EventHandler<char> CharacterReceived;

        // 特殊字符接收事件
        public event EventHandler<char> SpecialCharacterReceived;

        protected override void ProcessReceivedData(byte[] data)
        {
            try
            {
                // 解码为字符
                string text = _characterProtocol.GetEncoding().GetString(data);

                foreach (char c in text)
                {
                    // 触发字符接收事件
                    CharacterReceived?.Invoke(this, c);

                    // 检查是否是特殊字符
                    if (_characterProtocol.IsSpecialCharacter(c))
                    {
                        SpecialCharacterReceived?.Invoke(this, c);
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.ProtocolError, $"解析字符失败: {ex.Message}", ex);
            }

            // 调用基类处理
            base.ProcessReceivedData(data);
        }
    }

    // 使用示例
    public class CharacterProtocolExample
    {
        public static void Main(string[] args)
        {
            // 字符通信示例 - 基本用法
            using (CharacterSerialCommunicator charComm = new CharacterSerialCommunicator("COM1", 9600))
            {
                // 注册字符接收事件
                charComm.CharacterReceived += (sender, c) =>
                {
                    Console.WriteLine($"收到字符: {c}");
                };

                // 注册特殊字符接收事件
                charComm.SpecialCharacterReceived += (sender, c) =>
                {
                    Console.WriteLine($"收到特殊字符: {c}");
                };

                charComm.Open();

                // 发送单个字符
                charComm.SendCharacter('A');

                // 发送字符串（按字符发送）
                charComm.SendStringAsCharacters("Hello, character communication!");
            }

            // 字符通信示例 - 特殊字符处理
            using (CharacterSerialCommunicator charComm = new CharacterSerialCommunicator(
                "COM1", 9600, Parity.None, 8, StopBits.One,
                Encoding.ASCII, new[] { '\r', '\n', '!' }))
            {
                charComm.Open();

                // 发送包含特殊字符的文本
                charComm.SendStringAsCharacters("Hello!\r\nThis is a test.");
            }

        }
    }
}
