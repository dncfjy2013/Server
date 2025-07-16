using System;
using System.Threading;
using System.Threading.Tasks;

namespace Entity.Communication.PLCComu.Common
{
    // PLC通讯基类
    public abstract class PlcComuBase : IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _operationSemaphore = new SemaphoreSlim(1, 1);
        private Timer _reconnectTimer;
        private bool _isDisposed;

        protected PlcComuBase(PlcConnectionParameters parameters)
        {
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            ConnectionState = PlcConnectionState.Disconnected;
        }

        public PlcConnectionParameters Parameters { get; }
        public PlcConnectionState ConnectionState { get; private set; }
        public event EventHandler<PlcConnectionState> ConnectionStateChanged;
        public event EventHandler<Exception> CommunicationError;

        // 连接方法
        public async Task ConnectAsync()
        {
            await _connectionSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                if (ConnectionState == PlcConnectionState.Connected)
                    return;

                UpdateConnectionState(PlcConnectionState.Connecting);
                try
                {
                    await PerformConnectAsync(_cancellationTokenSource.Token);
                    UpdateConnectionState(PlcConnectionState.Connected);
                    StartReconnectTimer(false);
                }
                catch (OperationCanceledException)
                {
                    UpdateConnectionState(PlcConnectionState.Disconnected);
                    throw;
                }
                catch (Exception ex)
                {
                    UpdateConnectionState(PlcConnectionState.Error);
                    OnCommunicationError(ex);
                    StartReconnectTimer(true);
                    throw new PlcCommunicationException("连接失败", ex);
                }
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        // 断开连接方法
        public async Task DisconnectAsync()
        {
            await _connectionSemaphore.WaitAsync();
            try
            {
                if (ConnectionState == PlcConnectionState.Disconnected)
                    return;

                UpdateConnectionState(PlcConnectionState.Disconnecting);
                StopReconnectTimer();

                try
                {
                    await PerformDisconnectAsync();
                    UpdateConnectionState(PlcConnectionState.Disconnected);
                }
                catch (Exception ex)
                {
                    UpdateConnectionState(PlcConnectionState.Error);
                    OnCommunicationError(ex);
                    throw new PlcCommunicationException("断开连接失败", ex);
                }
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        // 读取数据方法
        public async Task<byte[]> ReadAsync(string address, int length)
        {
            if (ConnectionState != PlcConnectionState.Connected)
                throw new PlcCommunicationException("未连接到PLC");

            await _operationSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                try
                {
                    return await PerformReadAsync(address, length, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await HandleCommunicationErrorAsync(ex);
                    throw new PlcCommunicationException($"读取失败: {address}", ex);
                }
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        // 写入数据方法
        public async Task WriteAsync(string address, byte[] data)
        {
            if (ConnectionState != PlcConnectionState.Connected)
                throw new PlcCommunicationException("未连接到PLC");

            await _operationSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                try
                {
                    await PerformWriteAsync(address, data, _cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await HandleCommunicationErrorAsync(ex);
                    throw new PlcCommunicationException($"写入失败: {address}", ex);
                }
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        // 释放资源
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                    _connectionSemaphore.Dispose();
                    _operationSemaphore.Dispose();
                    StopReconnectTimer();
                }

                _isDisposed = true;
            }
        }

        // 抽象方法：执行连接操作
        protected abstract Task PerformConnectAsync(CancellationToken cancellationToken);

        // 抽象方法：执行断开连接操作
        protected abstract Task PerformDisconnectAsync();

        // 抽象方法：执行读取操作
        protected abstract Task<byte[]> PerformReadAsync(string address, int length, CancellationToken cancellationToken);

        // 抽象方法：执行写入操作
        protected abstract Task PerformWriteAsync(string address, byte[] data, CancellationToken cancellationToken);

        // 更新连接状态
        private void UpdateConnectionState(PlcConnectionState newState)
        {
            if (ConnectionState != newState)
            {
                ConnectionState = newState;
                ConnectionStateChanged?.Invoke(this, newState);
            }
        }

        // 启动重连定时器
        private void StartReconnectTimer(bool immediateAttempt)
        {
            StopReconnectTimer();

            int dueTime = immediateAttempt ? 0 : 5000;
            _reconnectTimer = new Timer(async _ => await TryReconnectAsync(), null, dueTime, Timeout.Infinite);
        }

        // 停止重连定时器
        private void StopReconnectTimer()
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
        }

        // 尝试重连
        private async Task TryReconnectAsync()
        {
            if (_cancellationTokenSource.IsCancellationRequested)
                return;

            try
            {
                if (ConnectionState != PlcConnectionState.Connected &&
                    ConnectionState != PlcConnectionState.Connecting)
                {
                    await ConnectAsync();
                }
            }
            catch (Exception ex)
            {
                OnCommunicationError(ex);
                StartReconnectTimer(true);
            }
        }

        // 处理通讯错误
        private async Task HandleCommunicationErrorAsync(Exception ex)
        {
            try
            {
                await DisconnectAsync();
            }
            catch (Exception disconnectEx)
            {
                OnCommunicationError(new AggregateException(ex, disconnectEx));
            }

            StartReconnectTimer(true);
            OnCommunicationError(ex);
        }

        // 触发通讯错误事件
        private void OnCommunicationError(Exception ex)
        {
            CommunicationError?.Invoke(this, ex);
        }
    }
}
