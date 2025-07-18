using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.CANComu.Common
{
    // CAN消息接收事件参数
    public class CANMessageReceivedEventArgs : EventArgs
    {
        public CANMessage Message { get; }

        public CANMessageReceivedEventArgs(CANMessage message)
        {
            Message = message;
        }
    }
}
