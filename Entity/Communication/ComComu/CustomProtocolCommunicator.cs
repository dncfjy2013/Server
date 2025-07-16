using Entity.Communication.ComComu.Common;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu
{
    // 自定义帧头帧尾协议
    public class CustomFramingProtocol : IProtocol
    {
        public string Name => "Custom Framing";

        private readonly byte[] _startMarker;
        private readonly byte[] _endMarker;
        private readonly bool _includeMarkersInMessage;

        public CustomFramingProtocol(byte[] startMarker, byte[] endMarker, bool includeMarkersInMessage = false)
        {
            _startMarker = startMarker ?? throw new ArgumentNullException(nameof(startMarker));
            _endMarker = endMarker ?? throw new ArgumentNullException(nameof(endMarker));
            _includeMarkersInMessage = includeMarkersInMessage;
        }

        public byte[] EncodeMessage(byte[] data)
        {
            byte[] encoded = new byte[_startMarker.Length + data.Length + _endMarker.Length];
            Array.Copy(_startMarker, 0, encoded, 0, _startMarker.Length);
            Array.Copy(data, 0, encoded, _startMarker.Length, data.Length);
            Array.Copy(_endMarker, 0, encoded, _startMarker.Length + data.Length, _endMarker.Length);
            return encoded;
        }

        public byte[] DecodeMessage(byte[] data)
        {
            if (!_includeMarkersInMessage)
                return data;

            // 移除帧头和帧尾
            if (data.Length < _startMarker.Length + _endMarker.Length)
                return new byte[0];

            byte[] decoded = new byte[data.Length - _startMarker.Length - _endMarker.Length];
            Array.Copy(data, _startMarker.Length, decoded, 0, decoded.Length);
            return decoded;
        }

        public bool ValidateMessage(byte[] data)
        {
            // 验证帧头
            if (data.Length < _startMarker.Length + _endMarker.Length)
                return false;

            for (int i = 0; i < _startMarker.Length; i++)
            {
                if (data[i] != _startMarker[i])
                    return false;
            }

            // 验证帧尾
            for (int i = 0; i < _endMarker.Length; i++)
            {
                if (data[data.Length - _endMarker.Length + i] != _endMarker[i])
                    return false;
            }

            return true;
        }

        public int GetMessageLength(byte[] buffer)
        {
            if (buffer.Length < _startMarker.Length + _endMarker.Length)
                return -1;

            // 查找帧头
            int startIndex = IndexOf(buffer, _startMarker, 0);
            if (startIndex < 0)
                return -1;

            // 从帧头后查找帧尾
            int searchIndex = startIndex + _startMarker.Length;
            while (searchIndex <= buffer.Length - _endMarker.Length)
            {
                if (SequenceEquals(buffer, searchIndex, _endMarker))
                {
                    return searchIndex + _endMarker.Length;
                }
                searchIndex++;
            }

            return -1;
        }

        private int IndexOf(byte[] buffer, byte[] pattern, int startIndex)
        {
            for (int i = startIndex; i <= buffer.Length - pattern.Length; i++)
            {
                if (SequenceEquals(buffer, i, pattern))
                    return i;
            }
            return -1;
        }

        private bool SequenceEquals(byte[] buffer, int startIndex, byte[] pattern)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (buffer[startIndex + i] != pattern[i])
                    return false;
            }
            return true;
        }
    }
    // 自定义协议通信类
    public class CustomProtocolCommunicator : SerialComuBase
    {
        public CustomProtocolCommunicator(string portName, int baudRate = 9600,
            Parity parity = Parity.None, int dataBits = 8,
            StopBits stopBits = StopBits.One, byte[] startMarker = null, byte[] endMarker = null)
            : base(portName, baudRate, parity, dataBits, stopBits)
        {
            // 设置默认帧头帧尾
            startMarker = startMarker ?? new byte[] { 0xAA, 0x55 };
            endMarker = endMarker ?? new byte[] { 0x0D, 0x0A };

            // 设置自定义协议
            Protocol = new CustomFramingProtocol(startMarker, endMarker);
        }
    }

    // 使用示例
    public class CustomProtocolExample
    {
        public static void Main(string[] args)
        {
            // 自定义协议通信示例
            using (CustomProtocolCommunicator customComm = new CustomProtocolCommunicator(
                "COM1", 9600, Parity.None, 8, StopBits.One,
                new byte[] { 0xAA, 0x55 }, new byte[] { 0x0D, 0x0A }))
            {
                customComm.Open();

                // 发送自定义消息
                customComm.Send(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            }
        }
    }
}
