using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.NamedPipe
{
    /// <summary>
    /// 命名管道服务器实现
    /// </summary>
    public class NamedPipeServer : INamedPipeServer
    {
        private readonly string _pipeName;
        private NamedPipeServerStream _serverStream;
        private PipeConnectionState _state;
        private bool _isRunning;
        private readonly object _syncLock = new object();

        public string PipeName => _pipeName;
        public PipeConnectionState State => _state;

        public event Action<PipeConnectionState, string> StateChanged;
        public event Action<string> DataReceived;
        public event Action<byte[]> RawDataReceived;

        public NamedPipeServer(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
                throw new ArgumentException("管道名称不能为空", nameof(pipeName));
            _pipeName = pipeName;
            _state = PipeConnectionState.Disconnected;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning) return;

            _isRunning = true;
            UpdateState(PipeConnectionState.Connecting, "服务器启动，等待客户端连接...");

            try
            {
                while (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    // 创建命名管道（支持双向通信，允许多个客户端）
                    using (_serverStream = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous))
                    {
                        try
                        {
                            // 等待客户端连接（异步）
                            await _serverStream.WaitForConnectionAsync(cancellationToken);
                            UpdateState(PipeConnectionState.Connected, "客户端已连接");

                            // 启动数据接收循环
                            await ReceiveLoopAsync(cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            // 正常取消
                            break;
                        }
                        catch (Exception ex)
                        {
                            UpdateState(PipeConnectionState.Error, $"连接错误: {ex.Message}");
                            await Task.Delay(1000, cancellationToken); // 延迟后重试
                        }
                        finally
                        {
                            if (_serverStream.IsConnected)
                                _serverStream.Disconnect();
                            UpdateState(PipeConnectionState.Disconnected, "客户端已断开");
                        }
                    }
                }
            }
            finally
            {
                _isRunning = false;
                UpdateState(PipeConnectionState.Disconnected, "服务器已停止");
            }
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            if (_serverStream?.IsConnected == true)
            {
                _serverStream.Disconnect();
            }
            await Task.CompletedTask;
        }

        public async Task<bool> SendAsync(string data, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(data))
                throw new ArgumentException("发送数据不能为空", nameof(data));
            byte[] bytes = Encoding.UTF8.GetBytes(data);
            return await SendRawAsync(bytes, cancellationToken);
        }

        public async Task<bool> SendRawAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("发送数据不能为空", nameof(data));
            if (_state != PipeConnectionState.Connected || _serverStream?.IsConnected != true)
            {
                UpdateState(PipeConnectionState.Error, "未连接到客户端，发送失败");
                return false;
            }

            try
            {
                // 发送数据协议：前缀4字节表示数据长度（大端序）+ 实际数据
                byte[] lengthPrefix = BitConverter.GetBytes(data.Length);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lengthPrefix); // 转换为大端序

                // 发送长度前缀
                await _serverStream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length, cancellationToken);
                // 发送数据内容
                await _serverStream.WriteAsync(data, 0, data.Length, cancellationToken);
                await _serverStream.FlushAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                UpdateState(PipeConnectionState.Error, $"发送失败: {ex.Message}");
                return false;
            }
        }

        // 数据接收循环
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (_isRunning && _serverStream?.IsConnected == true && !cancellationToken.IsCancellationRequested)
                {
                    // 读取长度前缀（4字节）
                    byte[] lengthPrefix = new byte[4];
                    int bytesRead = await _serverStream.ReadAsync(lengthPrefix, 0, lengthPrefix.Length, cancellationToken);
                    if (bytesRead != 4)
                    {
                        UpdateState(PipeConnectionState.Error, "接收长度前缀失败");
                        break;
                    }

                    // 转换为数据长度（大端序）
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(lengthPrefix);
                    int dataLength = BitConverter.ToInt32(lengthPrefix, 0);
                    if (dataLength <= 0 || dataLength > 1024 * 1024) // 限制最大1MB
                    {
                        UpdateState(PipeConnectionState.Error, $"无效的数据长度: {dataLength}");
                        break;
                    }

                    // 读取数据内容
                    byte[] data = new byte[dataLength];
                    int totalRead = 0;
                    while (totalRead < dataLength)
                    {
                        bytesRead = await _serverStream.ReadAsync(
                            data, totalRead, dataLength - totalRead, cancellationToken);
                        if (bytesRead == 0)
                        {
                            UpdateState(PipeConnectionState.Error, "数据接收中断");
                            return;
                        }
                        totalRead += bytesRead;
                    }

                    // 触发数据接收事件
                    RawDataReceived?.Invoke(data);
                    DataReceived?.Invoke(Encoding.UTF8.GetString(data));
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                UpdateState(PipeConnectionState.Error, $"接收错误: {ex.Message}");
            }
        }

        private void UpdateState(PipeConnectionState state, string message)
        {
            lock (_syncLock)
            {
                _state = state;
            }
            StateChanged?.Invoke(state, message);
        }

        public void Dispose()
        {
            _ = StopAsync();
            _serverStream?.Dispose();
        }
    }
}
