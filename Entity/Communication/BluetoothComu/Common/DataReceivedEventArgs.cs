using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.BluetoothComu.Common
{
    // 数据接收事件参数
    public class DataReceivedEventArgs : EventArgs
    {
        public byte[] Data { get; }
        public string DeviceId { get; }

        public DataReceivedEventArgs(byte[] data, string deviceId)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
        }
    }
}
