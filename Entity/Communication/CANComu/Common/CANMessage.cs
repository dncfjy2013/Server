using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.CANComu.Common
{
    // CAN消息结构
    public class CANMessage
    {
        public uint Id { get; set; }
        public bool IsExtendedFrame { get; set; }
        public bool IsRemoteFrame { get; set; }
        public byte[] Data { get; set; }
        public DateTime Timestamp { get; set; }

        public CANMessage()
        {
            Data = new byte[8];
        }

        public override string ToString()
        {
            string idFormat = IsExtendedFrame ? "X8" : "X3";
            string dataString = BitConverter.ToString(Data).Replace("-", " ");
            return $"ID: 0x{Id.ToString(idFormat)}, Data: {dataString}, Time: {Timestamp:HH:mm:ss.fff}";
        }
    }
}
