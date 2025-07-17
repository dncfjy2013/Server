using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.BluetoothComu.Common
{
    // 蓝牙通信接口 - 定义所有蓝牙传输方式必须实现的方法
    public interface IBluetoothTransport : IDisposable
    {
        Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);
        Task<int> SendDataAsync(byte[] data, CancellationToken cancellationToken = default);
        Task<byte[]> ReceiveDataAsync(int timeout = 5000, CancellationToken cancellationToken = default);
        bool IsConnected { get; }
        string DeviceId { get; }
        event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;
        event EventHandler<DataReceivedEventArgs> DataReceived;
    }
}
