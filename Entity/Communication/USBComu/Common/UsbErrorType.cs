using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.USBComu.Common
{
    // USB通信错误类型
    public enum UsbErrorType
    {
        DeviceNotFound,       // 设备未找到
        DeviceNotOpen,        // 设备未打开
        WriteFailed,          // 写入失败
        ReadFailed,           // 读取失败
        Timeout,              // 超时
        DeviceDisconnected,   // 设备断开连接
        ConfigurationError,   // 配置错误
        ProtocolError         // 协议错误
    }
}
