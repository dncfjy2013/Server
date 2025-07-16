using Entity.Communication.ComComu.Common;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu
{
    // Modbus RTU协议实现
    public class ModbusRtuProtocol : IProtocol
    {
        public string Name => "Modbus RTU";

        public byte[] EncodeMessage(byte[] data)
        {
            ushort crc = CalculateCrc(data);
            byte[] message = new byte[data.Length + 2];
            Array.Copy(data, 0, message, 0, data.Length);
            message[data.Length] = (byte)(crc & 0xFF);
            message[data.Length + 1] = (byte)(crc >> 8);
            return message;
        }

        public byte[] DecodeMessage(byte[] data)
        {
            if (data.Length < 3)
                return new byte[0];

            return data.Take(data.Length - 2).ToArray();
        }

        public bool ValidateMessage(byte[] data)
        {
            if (data.Length < 3)
                return false;

            ushort receivedCrc = (ushort)(data[data.Length - 2] | data[data.Length - 1] << 8);
            ushort calculatedCrc = CalculateCrc(data.Take(data.Length - 2).ToArray());

            return receivedCrc == calculatedCrc;
        }

        public int GetMessageLength(byte[] buffer)
        {
            // Modbus RTU没有固定消息长度，需要足够的数据才能计算CRC
            if (buffer.Length >= 5)
                return buffer.Length; // 最小Modbus消息长度

            return -1;
        }

        private ushort CalculateCrc(byte[] data)
        {
            ushort crc = 0xFFFF;

            foreach (byte b in data)
            {
                crc ^= b;

                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xA001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return crc;
        }
    }

    // Modbus RTU通信类
    public class ModbusRtuCommunicator : SerialComuBase
    {
        public ModbusRtuCommunicator(string portName, int baudRate = 9600,
            Parity parity = Parity.None, int dataBits = 8,
            StopBits stopBits = StopBits.One)
            : base(portName, baudRate, parity, dataBits, stopBits)
        {
            // 设置Modbus RTU协议
            Protocol = new ModbusRtuProtocol();
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
    public class ModbusRtuProtocolExample
    {
        public static void Main(string[] args)
        {
            // Modbus RTU通信示例
            using (ModbusRtuCommunicator modbusComm = new ModbusRtuCommunicator("COM1", 9600))
            {
                modbusComm.Open();

                // 读取保持寄存器
                byte[] response = modbusComm.ReadHoldingRegisters(1, 0, 5);

                // 解析响应...
            }
        }
    }
}
