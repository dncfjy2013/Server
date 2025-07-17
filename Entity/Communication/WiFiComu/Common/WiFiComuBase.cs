using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entity.Communication.WiFiComu.Common
{
    // 抽象WiFi通信类 - 提供基础实现
    public abstract class WiFiComuBase : IWiFiTransport
    {
        protected readonly CancellationTokenSource _connectionCts = new CancellationTokenSource();
        protected WiFiConnectionState _connectionState = WiFiConnectionState.Disconnected;
        protected string _connectedSSID = null;
        protected bool _disposed = false;

        public WiFiConnectionState ConnectionState => _connectionState;
        public string ConnectedSSID => _connectedSSID ?? throw new InvalidOperationException("未连接到WiFi网络");
        public bool IsConnected => _connectionState == WiFiConnectionState.Connected && !_connectionCts.IsCancellationRequested;

        public event EventHandler<WiFiConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<WiFiDataReceivedEventArgs> DataReceived;

        public async Task<bool> ConnectAsync(WiFiConnectionParameters parameters, CancellationToken cancellationToken = default)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            if (string.IsNullOrWhiteSpace(parameters.SSID))
                throw new ArgumentException("WiFi SSID不能为空", nameof(parameters.SSID));

            if (IsConnected)
            {
                if (_connectedSSID == parameters.SSID)
                {
                    return true; // 已经连接到同一网络
                }

                await DisconnectAsync(cancellationToken);
            }

            try
            {
                UpdateConnectionState(WiFiConnectionState.Connecting);

                // 使用联合取消令牌
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _connectionCts.Token, cancellationToken);

                // 调用具体实现
                bool result = await ConnectCoreAsync(parameters, linkedCts.Token);

                if (result)
                {
                    _connectedSSID = parameters.SSID;
                    UpdateConnectionState(WiFiConnectionState.Connected);
                }
                else
                {
                    UpdateConnectionState(WiFiConnectionState.Disconnected, "连接失败");
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                UpdateConnectionState(WiFiConnectionState.Disconnected, "连接操作被取消");
                throw;
            }
            catch (Exception ex)
            {
                UpdateConnectionState(WiFiConnectionState.Error, "连接过程中发生错误", ex);
                return false;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return;

            try
            {
                UpdateConnectionState(WiFiConnectionState.Disconnecting);

                // 取消连接令牌
                _connectionCts.Cancel();

                // 调用具体实现
                await DisconnectCoreAsync(cancellationToken);

                _connectedSSID = null;
                UpdateConnectionState(WiFiConnectionState.Disconnected);
            }
            catch (OperationCanceledException)
            {
                UpdateConnectionState(WiFiConnectionState.Disconnected, "断开连接操作被取消");
                throw;
            }
            catch (Exception ex)
            {
                UpdateConnectionState(WiFiConnectionState.Error, "断开连接过程中发生错误", ex);
            }
        }

        public async Task<int> SendDataAsync(string targetIP, int targetPort, byte[] data, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("未连接到WiFi网络，无法发送数据");

            if (string.IsNullOrWhiteSpace(targetIP))
                throw new ArgumentException("目标IP地址不能为空", nameof(targetIP));

            if (targetPort <= 0 || targetPort > 65535)
                throw new ArgumentException("无效的目标端口号", nameof(targetPort));

            if (data == null || data.Length == 0)
                throw new ArgumentException("发送数据不能为空", nameof(data));

            try
            {
                // 调用具体实现
                return await SendDataCoreAsync(targetIP, targetPort, data, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 发送失败时断开连接
                await DisconnectAsync(cancellationToken);
                throw new InvalidOperationException("数据发送失败", ex);
            }
        }

        public async Task<(byte[] Data, string SourceIP, int SourcePort)> ReceiveDataAsync(int timeout = 5000, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("未连接到WiFi网络，无法接收数据");

            if (timeout <= 0)
                throw new ArgumentException("超时时间必须大于0", nameof(timeout));

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                // 调用具体实现
                return await ReceiveDataCoreAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"接收数据超时（{timeout}ms）");
            }
            catch (Exception ex)
            {
                // 接收失败时断开连接
                await DisconnectAsync(cancellationToken);
                throw new InvalidOperationException("数据接收失败", ex);
            }
        }

        protected void UpdateConnectionState(WiFiConnectionState newState, string message = null, Exception error = null)
        {
            _connectionState = newState;
            try
            {
                ConnectionStateChanged?.Invoke(this,
                    new WiFiConnectionStateChangedEventArgs(newState, message, error));
            }
            catch (Exception ex)
            {
                // 记录日志或忽略事件处理异常
                Console.WriteLine($"WiFi连接状态变更事件处理失败: {ex.Message}");
            }
        }

        protected void OnDataReceived(byte[] data, string sourceIP, int sourcePort)
        {
            try
            {
                DataReceived?.Invoke(this,
                    new WiFiDataReceivedEventArgs(data, sourceIP, sourcePort));
            }
            catch (Exception ex)
            {
                // 记录日志或忽略事件处理异常
                Console.WriteLine($"WiFi数据接收事件处理失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 清理托管资源
                    try
                    {
                        if (IsConnected)
                        {
                            DisconnectAsync().Wait();
                        }

                        _connectionCts.Cancel();
                        _connectionCts.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"释放WiFi资源时发生异常: {ex.Message}");
                    }
                }

                // 清理非托管资源

                _disposed = true;
            }
        }

        // 需由具体实现类重写的核心方法
        protected abstract Task<bool> ConnectCoreAsync(WiFiConnectionParameters parameters, CancellationToken cancellationToken);
        protected abstract Task DisconnectCoreAsync(CancellationToken cancellationToken);
        protected abstract Task<int> SendDataCoreAsync(string targetIP, int targetPort, byte[] data, CancellationToken cancellationToken);
        protected abstract Task<(byte[] Data, string SourceIP, int SourcePort)> ReceiveDataCoreAsync(CancellationToken cancellationToken);
    }

}
