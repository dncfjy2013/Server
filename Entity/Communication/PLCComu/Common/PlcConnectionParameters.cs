using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.PLCComu.Common
{
    // 通讯参数基类
    public abstract class PlcConnectionParameters
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public int Timeout { get; set; } = 1000;
    }
}
