using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.BluetoothComu.Common
{
    // 连接状态变更事件参数
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public bool IsConnected { get; }
        public string DeviceId { get; }
        public Exception Error { get; }

        public ConnectionStateChangedEventArgs(bool isConnected, string deviceId, Exception error = null)
        {
            IsConnected = isConnected;
            DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            Error = error;
        }
    }
}
