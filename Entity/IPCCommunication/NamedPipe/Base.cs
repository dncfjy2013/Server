using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.NamedPipe
{
    /// <summary>
    /// 管道连接状态
    /// </summary>
    public enum PipeConnectionState
    {
        Disconnected,   // 未连接
        Connecting,     // 连接中
        Connected,      // 已连接
        Error           // 错误
    }

    /// <summary>
    /// 命名管道服务器接口
    /// </summary>
    public interface INamedPipeServer : IDisposable
    {
        string PipeName { get; }
        PipeConnectionState State { get; }
        event Action<PipeConnectionState, string> StateChanged; // 状态变化事件
        event Action<string> DataReceived; // 字符串数据接收事件
        event Action<byte[]> RawDataReceived; // 原始字节数据接收事件
        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync();
        Task<bool> SendAsync(string data, CancellationToken cancellationToken = default);
        Task<bool> SendRawAsync(byte[] data, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 命名管道客户端接口
    /// </summary>
    public interface INamedPipeClient : IDisposable
    {
        string PipeName { get; }
        string ServerName { get; } // 服务器名称（本地为"."）
        PipeConnectionState State { get; }
        event Action<PipeConnectionState, string> StateChanged; // 状态变化事件
        event Action<string> DataReceived; // 字符串数据接收事件
        event Action<byte[]> RawDataReceived; // 原始字节数据接收事件
        Task ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync();
        Task<bool> SendAsync(string data, CancellationToken cancellationToken = default);
        Task<bool> SendRawAsync(byte[] data, CancellationToken cancellationToken = default);
    }
}
