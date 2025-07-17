using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.WiFiComu.Common
{
    // WiFi连接状态变更事件参数
    public class WiFiConnectionStateChangedEventArgs : EventArgs
    {
        public WiFiConnectionState NewState { get; }
        public string Message { get; }
        public Exception Error { get; }

        public WiFiConnectionStateChangedEventArgs(WiFiConnectionState newState, string message = null, Exception error = null)
        {
            NewState = newState;
            Message = message;
            Error = error;
        }
    }
}
