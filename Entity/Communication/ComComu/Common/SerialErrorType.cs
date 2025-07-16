using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu.Common
{
    // 串口错误类型
    public enum SerialErrorType
    {
        CommunicationError,   // 通信错误
        ProtocolError,        // 协议错误
        Timeout,              // 超时
        BufferOverflow        // 缓冲区溢出
    }
}
