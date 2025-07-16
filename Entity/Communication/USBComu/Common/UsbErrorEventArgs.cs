using Entity.Communication.USBComu.Common.UsbCommunicationFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.USBComu.Common
{
    // USB错误事件参数
    public class UsbErrorEventArgs : EventArgs
    {
        public UsbErrorType ErrorType { get; }
        public string Message { get; }
        public Exception InnerException { get; }

        public UsbErrorEventArgs(UsbErrorType errorType, string message, Exception innerException = null)
        {
            ErrorType = errorType;
            Message = message;
            InnerException = innerException;
        }
    }
}
