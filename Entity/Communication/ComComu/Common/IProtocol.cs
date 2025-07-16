using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu.Common
{
    // 协议接口 - 所有通信协议必须实现此接口
    public interface IProtocol
    {
        string Name { get; }
        byte[] EncodeMessage(byte[] data);
        byte[] DecodeMessage(byte[] data);
        bool ValidateMessage(byte[] data);
        int GetMessageLength(byte[] buffer);
    }
}
