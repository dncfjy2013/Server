using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.NetComu.Common
{
    // 通信错误类型
    public enum CommunicationErrorType
    {
        ConnectionFailed,     // 连接失败
        SendFailed,           // 发送失败
        ReceiveFailed,        // 接收失败
        Timeout,              // 超时
        ProtocolError,        // 协议错误
        Disconnected          // 连接断开
    }
}
