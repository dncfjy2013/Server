using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.NetComu.Common
{
    // 通信状态
    public enum CommunicationStatus
    {
        Disconnected,         // 已断开
        Connecting,           // 正在连接
        Connected,            // 已连接
        Disconnecting         // 正在断开
    }
}
