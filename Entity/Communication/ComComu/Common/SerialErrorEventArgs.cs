using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.ComComu.Common
{
    // 串口错误事件参数
    public class SerialErrorEventArgs : EventArgs
    {
        public SerialErrorType ErrorType { get; }
        public string Message { get; }
        public Exception InnerException { get; }

        public SerialErrorEventArgs(SerialErrorType errorType, string message, Exception innerException = null)
        {
            ErrorType = errorType;
            Message = message;
            InnerException = innerException;
        }
    }
}
