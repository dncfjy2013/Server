using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.CANComu.Common
{
    // CAN总线连接状态变更事件参数
    public class CANConnectionStateChangedEventArgs : EventArgs
    {
        public CANConnectionState NewState { get; }
        public string Message { get; }
        public Exception Error { get; }

        public CANConnectionStateChangedEventArgs(CANConnectionState newState, string message = null, Exception error = null)
        {
            NewState = newState;
            Message = message;
            Error = error;
        }
    }
}
