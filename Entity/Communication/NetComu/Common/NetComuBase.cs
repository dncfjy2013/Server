using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Entity.Communication.NetComu.Common
{
    // 通信基类
    public abstract class NetComuBase : IDisposable
    {
        #region 成员变量
        protected readonly string _host;
        protected readonly int _port;
        protected readonly NetType _protocolType;
        protected readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        protected readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        protected readonly object _syncRoot = new object();
        protected CommunicationStatus _status = CommunicationStatus.Disconnected;
        protected bool _isDisposed;
        #endregion

        #region 属性
        /// <summary>
        /// 目标主机
        /// </summary>
        public string Host => _host;

        /// <summary>
        /// 目标端口
        /// </summary>
        public int Port => _port;

        /// <summary>
        /// 协议类型
        /// </summary>
        public NetType ProtocolType => _protocolType;

        /// <summary>
        /// 当前连接状态
        /// </summary>
        public CommunicationStatus Status => _status;

        /// <summary>
        /// 接收缓冲区大小
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 4096;

        /// <summary>
        /// 发送超时时间(毫秒)
        /// </summary>
        public int SendTimeout { get; set; } = 1000;

        /// <summary>
        /// 接收超时时间(毫秒)
        /// </summary>
        public int ReceiveTimeout { get; set; } = 2000;

        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 自动重连间隔(毫秒)
        /// </summary>
        public int ReconnectInterval { get; set; } = 5000;
        #endregion

        #region 事件
        /// <summary>
        /// 接收到数据事件
        /// </summary>
        public event EventHandler<byte[]> DataReceived;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event EventHandler<CommunicationErrorEventArgs> ErrorOccurred;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<CommunicationStatus> StatusChanged;
        #endregion

        #region 构造函数
        protected NetComuBase(string host, int port, NetType protocolType)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _port = port;
            _protocolType = protocolType;
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 打开连接
        /// </summary>
        public virtual Task OpenAsync()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (Status == CommunicationStatus.Connected || Status == CommunicationStatus.Connecting)
                return Task.CompletedTask;

            try
            {
                SetStatus(CommunicationStatus.Connecting);
                return ConnectAsync();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.ConnectionFailed, $"打开连接失败: {ex.Message}", ex);
                SetStatus(CommunicationStatus.Disconnected);
                throw;
            }
        }

        /// <summary>
        /// 关闭连接
        /// </summary>
        public virtual Task CloseAsync()
        {
            if (_isDisposed)
                return Task.CompletedTask;

            try
            {
                if (Status == CommunicationStatus.Disconnected || Status == CommunicationStatus.Disconnecting)
                    return Task.CompletedTask;

                SetStatus(CommunicationStatus.Disconnecting);
                return DisconnectAsync();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.ConnectionFailed, $"关闭连接失败: {ex.Message}", ex);
                SetStatus(CommunicationStatus.Disconnected);
                throw;
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public virtual Task SendAsync(byte[] data)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (data == null || data.Length == 0)
                return Task.CompletedTask;

            if (Status != CommunicationStatus.Connected)
                throw new InvalidOperationException("无法发送数据：未连接");

            try
            {
                _sendQueue.Enqueue(data);
                return ProcessSendQueueAsync();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.SendFailed, $"添加到发送队列失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 保护方法
        /// <summary>
        /// 连接实现
        /// </summary>
        protected abstract Task ConnectAsync();

        /// <summary>
        /// 断开连接实现
        /// </summary>
        protected abstract Task DisconnectAsync();

        /// <summary>
        /// 发送队列处理
        /// </summary>
        protected abstract Task ProcessSendQueueAsync();

        /// <summary>
        /// 接收数据循环
        /// </summary>
        protected abstract Task ReceiveDataLoopAsync();

        /// <summary>
        /// 处理接收到的数据
        /// </summary>
        protected virtual void ProcessReceivedData(byte[] data)
        {
            try
            {
                DataReceived?.Invoke(this, data);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.ProtocolError, $"处理接收数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 当发生错误时调用
        /// </summary>
        protected virtual void OnErrorOccurred(CommunicationErrorType errorType, string message, Exception innerException = null)
        {
            try
            {
                ErrorOccurred?.Invoke(this, new CommunicationErrorEventArgs(errorType, message, innerException));

                // 如果启用自动重连，尝试重新连接
                if (AutoReconnect &&
                    (errorType == CommunicationErrorType.ConnectionFailed ||
                     errorType == CommunicationErrorType.ReceiveFailed ||
                     errorType == CommunicationErrorType.Disconnected))
                {
                    ReconnectAsync();
                }
            }
            catch (Exception ex)
            {
                // 防止错误处理中再次出错
                Console.WriteLine($"错误处理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置连接状态
        /// </summary>
        protected virtual void SetStatus(CommunicationStatus newStatus)
        {
            if (_status != newStatus)
            {
                _status = newStatus;
                StatusChanged?.Invoke(this, newStatus);
            }
        }

        /// <summary>
        /// 尝试重新连接
        /// </summary>
        protected async Task ReconnectAsync()
        {
            try
            {
                // 确保已断开
                await CloseAsync();

                // 等待一段时间
                await Task.Delay(ReconnectInterval);

                // 尝试重新连接
                await OpenAsync();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.ConnectionFailed, $"重新连接失败: {ex.Message}", ex);
            }
        }
        #endregion

        #region 私有方法
        #endregion

        #region 释放资源
        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    CloseAsync().Wait();
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }

                _isDisposed = true;
            }
        }
        #endregion
    }
}
