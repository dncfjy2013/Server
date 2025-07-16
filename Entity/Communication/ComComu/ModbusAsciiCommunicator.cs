using Entity.Communication.ComComu.Common;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu
{
    // Modbus ASCII协议实现
    public class ModbusAsciiProtocol : IProtocol
    {
        public string Name => "Modbus ASCII";

        public byte[] EncodeMessage(byte[] data)
        {
            // 计算LRC校验
            byte lrc = CalculateLrc(data);

            // 构建ASCII消息
            StringBuilder sb = new StringBuilder();
            sb.Append(':'); // 起始符

            // 将数据转换为ASCII十六进制字符串
            foreach (byte b in data)
            {
                sb.Append(b.ToString("X2"));
            }

            // 添加LRC校验
            sb.Append(lrc.ToString("X2"));
            sb.Append("\r\n"); // 结束符

            // 返回ASCII字节数组
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        public byte[] DecodeMessage(byte[] data)
        {
            if (data.Length < 5) // 至少需要 ':' + 数据 + LRC + CR + LF
                return new byte[0];

            // 移除起始符和结束符
            string hexString = Encoding.ASCII.GetString(data, 1, data.Length - 3);

            // 提取LRC
            string lrcStr = hexString.Substring(hexString.Length - 2);
            byte receivedLrc = byte.Parse(lrcStr, System.Globalization.NumberStyles.HexNumber);

            // 提取数据
            string dataStr = hexString.Substring(0, hexString.Length - 2);
            byte[] decodedData = new byte[dataStr.Length / 2];

            for (int i = 0; i < decodedData.Length; i++)
            {
                decodedData[i] = byte.Parse(dataStr.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
            }

            // 验证LRC
            byte calculatedLrc = CalculateLrc(decodedData);
            if (calculatedLrc != receivedLrc)
                throw new InvalidOperationException("LRC校验失败");

            return decodedData;
        }

        public bool ValidateMessage(byte[] data)
        {
            if (data.Length < 5) // 至少需要 ':' + 数据 + LRC + CR + LF
                return false;

            // 检查起始符和结束符
            if (data[0] != (byte)':' ||
                data[data.Length - 2] != (byte)'\r' ||
                data[data.Length - 1] != (byte)'\n')
                return false;

            try
            {
                // 尝试解码，如果成功则有效
                DecodeMessage(data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public int GetMessageLength(byte[] buffer)
        {
            // 查找消息结束符
            for (int i = 1; i < buffer.Length - 1; i++)
            {
                if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n')
                    return i + 2;
            }

            return -1;
        }

        private byte CalculateLrc(byte[] data)
        {
            byte lrc = 0;
            foreach (byte b in data)
            {
                lrc += b;
            }
            return (byte)-(sbyte)lrc; // 取补码
        }
    }

    // Modbus ASCII通信类
    public class ModbusAsciiCommunicator : SerialComuBase
    {
        public ModbusAsciiCommunicator(string portName, int baudRate = 9600,
            Parity parity = Parity.None, int dataBits = 8,
            StopBits stopBits = StopBits.One)
            : base(portName, baudRate, parity, dataBits, stopBits)
        {
            // 设置Modbus ASCII协议
            Protocol = new ModbusAsciiProtocol();
        }

        // 读取保持寄存器
        public byte[] ReadHoldingRegisters(byte slaveAddress, ushort startAddress, ushort quantity)
        {
            byte[] request = new byte[6];
            request[0] = slaveAddress;
            request[1] = 0x03; // 功能码: 读保持寄存器
            request[2] = (byte)(startAddress >> 8);
            request[3] = (byte)(startAddress & 0xFF);
            request[4] = (byte)(quantity >> 8);
            request[5] = (byte)(quantity & 0xFF);

            return SendModbusRequest(request);
        }

        // 写入单个寄存器
        public byte[] WriteSingleRegister(byte slaveAddress, ushort address, ushort value)
        {
            byte[] request = new byte[6];
            request[0] = slaveAddress;
            request[1] = 0x06; // 功能码: 写单个寄存器
            request[2] = (byte)(address >> 8);
            request[3] = (byte)(address & 0xFF);
            request[4] = (byte)(value >> 8);
            request[5] = (byte)(value & 0xFF);

            return SendModbusRequest(request);
        }

        // 发送Modbus请求并接收响应
        private byte[] SendModbusRequest(byte[] request)
        {
            // 创建一个临时缓冲区来接收响应
            byte[] response = null;
            ManualResetEvent responseReceived = new ManualResetEvent(false);

            // 注册数据接收事件
            EventHandler<byte[]> handler = null;
            handler = (sender, data) =>
            {
                response = data;
                responseReceived.Set();
                DataReceived -= handler; // 移除事件处理程序
            };

            DataReceived += handler;

            try
            {
                // 发送请求
                Send(request);

                // 等待响应或超时
                if (!responseReceived.WaitOne(ReceiveTimeout))
                {
                    throw new TimeoutException("Modbus请求超时");
                }

                // 验证响应
                if (response == null || response.Length < 2)
                {
                    throw new Exception("接收到无效的Modbus响应");
                }

                // 检查是否有异常
                if ((response[0] & 0x80) != 0)
                {
                    byte errorCode = response[1];
                    throw new Exception($"Modbus异常: 功能码={response[0] & 0x7F}, 错误码={errorCode}");
                }

                return response;
            }
            finally
            {
                // 确保事件处理程序被移除
                DataReceived -= handler;
                responseReceived.Dispose();
            }
        }
    }

    // 使用示例
    public class ModbusAsciiProtocolExample
    {
        public static void Main(string[] args)
        {
            // Modbus ASCII通信示例
            using (ModbusAsciiCommunicator modbusAsciiComm = new ModbusAsciiCommunicator("COM1", 9600))
            {
                modbusAsciiComm.Open();

                // 读取保持寄存器
                byte[] response = modbusAsciiComm.ReadHoldingRegisters(1, 0, 5);

                // 处理响应...
            }
        }
    }
}
