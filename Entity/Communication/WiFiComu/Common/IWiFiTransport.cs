using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.WiFiComu.Common
{
    // WiFi通信接口
    public interface IWiFiTransport : IDisposable
    {
        // 属性
        WiFiConnectionState ConnectionState { get; }
        string ConnectedSSID { get; }
        bool IsConnected { get; }

        // 事件
        event EventHandler<WiFiConnectionStateChangedEventArgs> ConnectionStateChanged;
        event EventHandler<WiFiDataReceivedEventArgs> DataReceived;

        // 方法
        Task<bool> ConnectAsync(WiFiConnectionParameters parameters, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);
        Task<int> SendDataAsync(string targetIP, int targetPort, byte[] data, CancellationToken cancellationToken = default);
        Task<(byte[] Data, string SourceIP, int SourcePort)> ReceiveDataAsync(int timeout = 5000, CancellationToken cancellationToken = default);
    }
}
