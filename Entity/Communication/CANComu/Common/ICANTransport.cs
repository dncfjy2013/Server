using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.CANComu.Common
{
    // CAN总线通信接口
    public interface ICANTransport : IDisposable
    {
        // 属性
        CANConnectionState ConnectionState { get; }
        bool IsConnected { get; }

        // 事件
        event EventHandler<CANConnectionStateChangedEventArgs> ConnectionStateChanged;
        event EventHandler<CANMessageReceivedEventArgs> MessageReceived;

        // 方法
        Task<bool> ConnectAsync(CANConnectionParameters parameters, CancellationToken cancellationToken = default);
        Task DisconnectAsync(CancellationToken cancellationToken = default);
        Task<bool> SendMessageAsync(CANMessage message, CancellationToken cancellationToken = default);
        Task<CANMessage> ReceiveMessageAsync(int timeout = 5000, CancellationToken cancellationToken = default);
        Task SetFilterAsync(uint id, uint mask, bool isExtended = false, CancellationToken cancellationToken = default);
    }
}
