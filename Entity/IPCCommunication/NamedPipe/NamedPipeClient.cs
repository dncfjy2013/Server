using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.NamedPipe
{
    /// <summary>
    /// 命名管道客户端实现
    /// </summary>
    public class NamedPipeClient : INamedPipeClient
    {
        private readonly string _pipeName;
        private readonly string _serverName;
        private NamedPipeClientStream _clientStream;
        private PipeConnectionState _state;
        private bool _isConnected;
        private readonly object _syncLock = new object();

        public string PipeName => _pipeName;
        public string ServerName => _serverName;
        public PipeConnectionState State => _state;

        public event Action<PipeConnectionState, string> StateChanged;
        public event Action<string> DataReceived;
        public event Action<byte[]> RawDataReceived;

        public NamedPipeClient(string pipeName, string serverName = ".")
        {
            if (string.IsNullOrWhiteSpace(pipeName))
                throw new ArgumentException("管道名称不能为空", nameof(pipeName));
            if (string.IsNullOrWhiteSpace(serverName))
                throw new ArgumentException("服务器名称不能为空", nameof(serverName));
            _pipeName = pipeName;
            _serverName = serverName;
            _state = PipeConnectionState.Disconnected;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_isConnected) return;

            UpdateState(PipeConnectionState.Connecting, $"连接到管道 {_serverName}\\{_pipeName}...");

            try
            {
                // 创建客户端管道流
                _clientStream = new NamedPipeClientStream(
                    _serverName,
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                // 连接到服务器（超时30秒）
                await _clientStream.ConnectAsync(30000, cancellationToken);
                _isConnected = true;

                UpdateState(PipeConnectionState.Connected, "已连接到服务器");

                // 启动数据接收循环
                _ = ReceiveLoopAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                UpdateState(PipeConnectionState.Disconnected, "连接被取消");
                throw;
            }
            catch (Exception ex)
            {
                UpdateState(PipeConnectionState.Error, $"连接失败: {ex.Message}");
                DisposeStream();
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            _isConnected = false;
            if (_clientStream?.IsConnected == true)
            {
                _clientStream.Close();
            }
            UpdateState(PipeConnectionState.Disconnected, "已断开连接");
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
            if (!_isConnected || _clientStream?.IsConnected != true)
            {
                UpdateState(PipeConnectionState.Error, "未连接到服务器，发送失败");
                return false;
            }

            try
            {
                // 与服务器端一致的协议：长度前缀（4字节大端序）+ 数据
                byte[] lengthPrefix = BitConverter.GetBytes(data.Length);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(lengthPrefix);

                await _clientStream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length, cancellationToken);
                await _clientStream.WriteAsync(data, 0, data.Length, cancellationToken);
                await _clientStream.FlushAsync(cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                UpdateState(PipeConnectionState.Error, $"发送失败: {ex.Message}");
                _ = DisconnectAsync(); // 发送失败时自动断开
                return false;
            }
        }

        // 数据接收循环
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (_isConnected && _clientStream?.IsConnected == true && !cancellationToken.IsCancellationRequested)
                {
                    // 读取长度前缀（4字节）
                    byte[] lengthPrefix = new byte[4];
                    int bytesRead = await _clientStream.ReadAsync(lengthPrefix, 0, lengthPrefix.Length, cancellationToken);
                    if (bytesRead != 4)
                    {
                        UpdateState(PipeConnectionState.Error, "接收长度前缀失败");
                        break;
                    }

                    // 解析数据长度
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(lengthPrefix);
                    int dataLength = BitConverter.ToInt32(lengthPrefix, 0);
                    if (dataLength <= 0 || dataLength > 1024 * 1024)
                    {
                        UpdateState(PipeConnectionState.Error, $"无效的数据长度: {dataLength}");
                        break;
                    }

                    // 读取数据内容
                    byte[] data = new byte[dataLength];
                    int totalRead = 0;
                    while (totalRead < dataLength)
                    {
                        bytesRead = await _clientStream.ReadAsync(
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
            finally
            {
                if (_isConnected)
                {
                    _ = DisconnectAsync(); // 接收异常时自动断开
                }
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

        private void DisposeStream()
        {
            _clientStream?.Dispose();
            _clientStream = null;
        }

        public void Dispose()
        {
            _ = DisconnectAsync();
            DisposeStream();
        }
    }
}
