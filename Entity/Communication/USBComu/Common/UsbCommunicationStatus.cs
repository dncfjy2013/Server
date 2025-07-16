using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.USBComu.Common
{
    // USB通信状态
    public enum UsbCommunicationStatus
    {
        Disconnected,         // 已断开
        Connecting,           // 正在连接
        Connected,            // 已连接
        Disconnecting         // 正在断开
    }
}
