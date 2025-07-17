using Entity.Communication.BluetoothComu.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.BluetoothComu
{
    // 经典蓝牙传输实现示例
    public class ClassicBluetoothTransport : IBluetoothTransport
    {
        private readonly CancellationTokenSource _connectionCts = new CancellationTokenSource();
        private bool _isConnected = false;
        private string _deviceId = null;
        private bool _disposed = false;

        public bool IsConnected => _isConnected && !_connectionCts.IsCancellationRequested;
        public string DeviceId => _deviceId ?? throw new InvalidOperationException("设备未连接");

        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;
        public event EventHandler<DataReceivedEventArgs> DataReceived;

        public async Task<bool> ConnectAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("设备ID不能为空", nameof(deviceId));

            if (IsConnected)
            {
                if (_deviceId == deviceId)
                {
                    return true; // 已经连接到同一设备
                }

                await DisconnectAsync(cancellationToken);
            }

            try
            {
                // 使用联合取消令牌
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    _connectionCts.Token, cancellationToken);

                // 模拟连接过程
                await Task.Delay(500, linkedCts.Token);

                _deviceId = deviceId;
                _isConnected = true;

                OnConnectionStateChanged(true, null);
                return true;
            }
            catch (OperationCanceledException)
            {
                _isConnected = false;
                OnConnectionStateChanged(false, null);
                throw;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                OnConnectionStateChanged(false, ex);
                return false;
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                return;

            try
            {
                // 取消连接令牌
                _connectionCts.Cancel();

                // 模拟断开连接过程
                await Task.Delay(200, cancellationToken);

                _isConnected = false;
                OnConnectionStateChanged(false, null);
            }
            catch (OperationCanceledException)
            {
                _isConnected = false;
                OnConnectionStateChanged(false, null);
                throw;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                OnConnectionStateChanged(false, ex);
            }
        }

        public async Task<int> SendDataAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("设备未连接，无法发送数据");

            if (data == null || data.Length == 0)
                throw new ArgumentException("发送数据不能为空", nameof(data));

            try
            {
                // 模拟数据发送
                await Task.Delay(100, cancellationToken);

                return data.Length;
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

        public async Task<byte[]> ReceiveDataAsync(int timeout = 5000, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("设备未连接，无法接收数据");

            if (timeout <= 0)
                throw new ArgumentException("超时时间必须大于0", nameof(timeout));

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                // 模拟数据接收
                await Task.Delay(200, timeoutCts.Token);

                // 返回模拟数据
                var mockData = System.Text.Encoding.UTF8.GetBytes("模拟接收数据");
                OnDataReceived(mockData);
                return mockData;
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

        private void OnConnectionStateChanged(bool isConnected, Exception error)
        {
            try
            {
                ConnectionStateChanged?.Invoke(this,
                    new ConnectionStateChangedEventArgs(isConnected, _deviceId ?? string.Empty, error));
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"[ClassicBluetoothTransport] 触发连接状态变更事件失败: {ex}");
            }
        }

        private void OnDataReceived(byte[] data)
        {
            try
            {
                DataReceived?.Invoke(this, new DataReceivedEventArgs(data, _deviceId));
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"[ClassicBluetoothTransport] 触发数据接收事件失败: {ex}");
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
                        //Debug.WriteLine($"[ClassicBluetoothTransport] 释放资源时发生异常: {ex}");
                    }
                }

                // 清理非托管资源

                _disposed = true;
            }
        }
    }

    // 使用示例
    public class ClassicBluetoothTest
    {
        private static ClassicBluetoothTransport _bluetoothTransport;
        private static readonly ManualResetEvent _connectionEvent = new ManualResetEvent(false);
        private static readonly ManualResetEvent _dataReceivedEvent = new ManualResetEvent(false);

        public static async Task Main()
        {
            try
            {
                Console.WriteLine("蓝牙通信示例程序启动...");

                // 创建蓝牙传输实例
                _bluetoothTransport = new ClassicBluetoothTransport();

                // 注册事件处理程序
                _bluetoothTransport.ConnectionStateChanged += OnConnectionStateChanged;
                _bluetoothTransport.DataReceived += OnDataReceived;

                // 连接到蓝牙设备
                string deviceId = "00:11:22:33:44:55"; // 替换为实际设备ID
                Console.WriteLine($"正在连接到设备: {deviceId}");

                bool connected = await _bluetoothTransport.ConnectAsync(deviceId);
                if (connected)
                {
                    Console.WriteLine("已成功连接到设备");

                    // 等待连接建立完成
                    _connectionEvent.WaitOne();

                    // 发送数据
                    string message = "Hello, Bluetooth!";
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    Console.WriteLine($"正在发送数据: {message}");

                    int bytesSent = await _bluetoothTransport.SendDataAsync(data);
                    Console.WriteLine($"已发送 {bytesSent} 字节数据");

                    // 接收数据
                    Console.WriteLine("等待接收数据...");
                    try
                    {
                        byte[] receivedData = await _bluetoothTransport.ReceiveDataAsync(timeout: 5000);
                        string receivedMessage = Encoding.UTF8.GetString(receivedData);
                        Console.WriteLine($"接收到数据: {receivedMessage}");
                    }
                    catch (TimeoutException)
                    {
                        Console.WriteLine("接收数据超时");
                    }

                    // 保持程序运行一段时间，等待可能的异步数据接收
                    Console.WriteLine("按任意键断开连接并退出...");
                    Console.ReadKey();
                }
                else
                {
                    Console.WriteLine("连接失败");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
            }
            finally
            {
                // 断开连接并释放资源
                if (_bluetoothTransport != null)
                {
                    Console.WriteLine("正在断开连接...");
                    await _bluetoothTransport.DisconnectAsync();
                    _bluetoothTransport.Dispose();
                    Console.WriteLine("已断开连接并释放资源");
                }

                // 清理事件
                _connectionEvent.Dispose();
                _dataReceivedEvent.Dispose();
            }
        }

        private static void OnConnectionStateChanged(object sender, ConnectionStateChangedEventArgs e)
        {
            Console.WriteLine($"连接状态变更: 已连接={e.IsConnected}, 设备ID={e.DeviceId}");
            if (e.Error != null)
            {
                Console.WriteLine($"错误信息: {e.Error.Message}");
            }

            if (e.IsConnected)
            {
                _connectionEvent.Set();
            }
        }

        private static void OnDataReceived(object sender, DataReceivedEventArgs e)
        {
            try
            {
                string message = Encoding.UTF8.GetString(e.Data);
                Console.WriteLine($"事件接收到数据: {message} (来自设备: {e.DeviceId})");
                _dataReceivedEvent.Set();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理接收数据时发生错误: {ex.Message}");
            }
        }
    }
}
