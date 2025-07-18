using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.CANComu.Common
{
    // CAN总线连接参数
    public class CANConnectionParameters
    {
        public string Channel { get; set; }
        public int BaudRate { get; set; } = 500000; // 默认500 kbps
        public Dictionary<string, string> ProtocolOptions { get; set; } = new Dictionary<string, string>();
    }
}
