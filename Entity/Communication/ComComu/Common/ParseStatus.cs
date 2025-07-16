using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu.Common
{
    // 协议解析状态
    public enum ParseStatus
    {
        Complete,       // 消息完整
        Incomplete,     // 消息不完整
        Invalid         // 消息无效
    }
}
