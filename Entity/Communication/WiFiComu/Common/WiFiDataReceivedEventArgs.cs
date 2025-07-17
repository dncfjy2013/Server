using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.WiFiComu.Common
{
    // WiFi数据接收事件参数
    public class WiFiDataReceivedEventArgs : EventArgs
    {
        public byte[] Data { get; }
        public string SourceIP { get; }
        public int SourcePort { get; }

        public WiFiDataReceivedEventArgs(byte[] data, string sourceIP, int sourcePort)
        {
            Data = data;
            SourceIP = sourceIP;
            SourcePort = sourcePort;
        }
    }
}
