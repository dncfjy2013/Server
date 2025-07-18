using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace Entity.Communication.CANComu.Common
{
    // 抽象CAN总线通信类 - 提供基础实现
    public abstract class CANTransportBase : ICANTransport
    {
        protected readonly CancellationTokenSource _connectionCts = new CancellationTokenSource();
        protected CANConnectionState _connectionState = CANConnectionState.Disconnected;
        protected bool _disposed = false;
        protected readonly object _syncRoot = new object();

        public CANConnectionState ConnectionState => _connectionState;
        public bool IsConnected => _connectionState == CANConnectionState.Connected && !_connectionCts.IsCancellationRequested;

        public event EventHandler<CANConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<CANMessageReceivedEventArgs> MessageReceived;

        public async Task<bool> ConnectAsync(CANConnectionParameters parameters, CancellationToken cancellationToken = default)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            if (string.IsNullOrWhiteSpace(parameters.Channel))
                throw new ArgumentException("CAN通道不能为空", nameof(parameters.Channel));

            if (IsConnected)
            {
                await DisconnectAsync(cancellationToken);
            }

            try
            {
                UpdateConnectionState(CANConnectionState.Connecting);

                // 使用联合取消令牌
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _connectionCts.Token, cancellationToken);

                // 调用具体实现
                bool result = await ConnectCoreAsync(parameters, linkedCts.Token);

                if (result)
                {
                    UpdateConnectionState(CANConnectionState.Connected);

                    // 启动消息接收循环
                    StartReceivingMessages();
                }
                else
                {
                    UpdateConnectionState(CANConnectionState.Disconnected, "连接失败");
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                UpdateConnectionState(CANConnectionState.Disconnected, "连接操作被取消");
                throw;
            }
            catch (Exception ex)
            {
                // 确保通信异常不会导致软件崩溃
                UpdateConnectionState(CANConnectionState.Error, "连接过程中发生错误", ex);
                return false;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return;

            try
            {
                UpdateConnectionState(CANConnectionState.Disconnecting);

                // 取消连接令牌
                _connectionCts.Cancel();

                // 调用具体实现
                await DisconnectCoreAsync(cancellationToken);

                UpdateConnectionState(CANConnectionState.Disconnected);
            }
            catch (OperationCanceledException)
            {
                UpdateConnectionState(CANConnectionState.Disconnected, "断开连接操作被取消");
                throw;
            }
            catch (Exception ex)
            {
                // 确保通信异常不会导致软件崩溃
                UpdateConnectionState(CANConnectionState.Error, "断开连接过程中发生错误", ex);
            }
        }

        public async Task<bool> SendMessageAsync(CANMessage message, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("未连接到CAN总线，无法发送消息");

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            try
            {
                // 调用具体实现
                return await SendMessageCoreAsync(message, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 确保通信异常不会导致软件崩溃
                UpdateConnectionState(CANConnectionState.Error, "发送消息过程中发生错误", ex);

                // 发送失败时尝试重新连接
                await TryReconnect();

                return false;
            }
        }

        public async Task<CANMessage> ReceiveMessageAsync(int timeout = 5000, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("未连接到CAN总线，无法接收消息");

            if (timeout <= 0)
                throw new ArgumentException("超时时间必须大于0", nameof(timeout));

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                // 调用具体实现
                return await ReceiveMessageCoreAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"接收消息超时（{timeout}ms）");
            }
            catch (Exception ex)
            {
                // 确保通信异常不会导致软件崩溃
                UpdateConnectionState(CANConnectionState.Error, "接收消息过程中发生错误", ex);

                // 接收失败时尝试重新连接
                await TryReconnect();

                throw new InvalidOperationException("数据接收失败", ex);
            }
        }

        public async Task SetFilterAsync(uint id, uint mask, bool isExtended = false, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("未连接到CAN总线，无法设置过滤器");

            try
            {
                // 调用具体实现
                await SetFilterCoreAsync(id, mask, isExtended, cancellationToken);
            }
            catch (Exception ex)
            {
                // 确保通信异常不会导致软件崩溃
                UpdateConnectionState(CANConnectionState.Error, "设置过滤器过程中发生错误", ex);
                throw;
            }
        }

        protected void UpdateConnectionState(CANConnectionState newState, string message = null, Exception error = null)
        {
            lock (_syncRoot)
            {
                _connectionState = newState;
            }

            try
            {
                ConnectionStateChanged?.Invoke(this,
                    new CANConnectionStateChangedEventArgs(newState, message, error));
            }
            catch (Exception ex)
            {
                // 确保事件处理异常不会导致软件崩溃
                Console.WriteLine($"CAN连接状态变更事件处理失败: {ex.Message}");
            }
        }

        protected void OnMessageReceived(CANMessage message)
        {
            try
            {
                MessageReceived?.Invoke(this, new CANMessageReceivedEventArgs(message));
            }
            catch (Exception ex)
            {
                // 确保事件处理异常不会导致软件崩溃
                Console.WriteLine($"CAN消息接收事件处理失败: {ex.Message}");
            }
        }

        private async void StartReceivingMessages()
        {
            // 在后台任务中接收消息，确保不会阻塞主线程
            _ = Task.Run(async () =>
            {
                try
                {
                    while (IsConnected)
                    {
                        try
                        {
                            // 持续接收消息
                            var message = await ReceiveMessageAsync(cancellationToken: _connectionCts.Token);
                            if (message != null)
                            {
                                OnMessageReceived(message);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // 正常取消，退出循环
                            break;
                        }
                        catch (TimeoutException)
                        {
                            // 超时可以忽略，继续接收
                        }
                        catch (Exception ex)
                        {
                            // 确保通信异常不会导致软件崩溃
                            Console.WriteLine($"接收CAN消息时发生错误: {ex.Message}");

                            // 发生错误时尝试重新连接
                            await TryReconnect();

                            // 短暂延迟后继续尝试接收
                            await Task.Delay(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CAN消息接收循环异常退出: {ex.Message}");
                }
            });
        }

        protected async Task TryReconnect()
        {
            // 注意：这里需要保存连接参数以便重新连接
            // 实际实现中可能需要更复杂的逻辑

            try
            {
                Console.WriteLine("尝试重新连接到CAN总线...");
                // 此处需要获取之前的连接参数
                // 简化示例，实际应用中需要改进

                // 等待一段时间后尝试重新连接
                await Task.Delay(2000);

                // 这里只是示例，实际实现需要获取正确的连接参数
                // await ConnectAsync(previousParameters);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重新连接失败: {ex.Message}");
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
                        Console.WriteLine($"释放CAN资源时发生异常: {ex.Message}");
                    }
                }

                // 清理非托管资源

                _disposed = true;
            }
        }

        // 需由具体实现类重写的核心方法
        protected abstract Task<bool> ConnectCoreAsync(CANConnectionParameters parameters, CancellationToken cancellationToken);
        protected abstract Task DisconnectCoreAsync(CancellationToken cancellationToken);
        protected abstract Task<bool> SendMessageCoreAsync(CANMessage message, CancellationToken cancellationToken);
        protected abstract Task<CANMessage> ReceiveMessageCoreAsync(CancellationToken cancellationToken);
        protected abstract Task SetFilterCoreAsync(uint id, uint mask, bool isExtended, CancellationToken cancellationToken);
    }

}
