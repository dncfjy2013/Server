using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.NetComu.Common
{
    // 通信错误事件参数
    public class CommunicationErrorEventArgs : EventArgs
    {
        public CommunicationErrorType ErrorType { get; }
        public string Message { get; }
        public Exception InnerException { get; }

        public CommunicationErrorEventArgs(CommunicationErrorType errorType, string message, Exception innerException = null)
        {
            ErrorType = errorType;
            Message = message;
            InnerException = innerException;
        }
    }
}
