using Entity.Communication.BluetoothComu.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.BluetoothComu
{
    // 低功耗蓝牙(BLE)传输实现示例
    public class BleBluetoothTransport : IBluetoothTransport
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
                await Task.Delay(700, linkedCts.Token);

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
                await Task.Delay(300, cancellationToken);

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
                await Task.Delay(150, cancellationToken);

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
                await Task.Delay(300, timeoutCts.Token);

                // 返回模拟数据
                var mockData = System.Text.Encoding.UTF8.GetBytes("模拟BLE接收数据");
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
                //Debug.WriteLine($"[BleBluetoothTransport] 触发连接状态变更事件失败: {ex}");
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
                //Debug.WriteLine($"[BleBluetoothTransport] 触发数据接收事件失败: {ex}");
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
                        //Debug.WriteLine($"[BleBluetoothTransport] 释放资源时发生异常: {ex}");
                    }
                }

                // 清理非托管资源

                _disposed = true;
            }
        }
    }

    // 使用示例
    public class BleBluetoothTest
    {
        private static BleBluetoothTransport _bleTransport;
        private static readonly ManualResetEvent _connectionEvent = new ManualResetEvent(false);
        private static readonly ManualResetEvent _disconnectionEvent = new ManualResetEvent(false);
        private static readonly ManualResetEvent _dataReceivedEvent = new ManualResetEvent(false);

        public static async Task Main()
        {
            try
            {
                Console.WriteLine("低功耗蓝牙(BLE)通信示例启动...");

                // 创建BLE传输实例
                _bleTransport = new BleBluetoothTransport();

                // 注册事件处理程序
                _bleTransport.ConnectionStateChanged += OnConnectionStateChanged;
                _bleTransport.DataReceived += OnDataReceived;

                // 连接到BLE设备
                string deviceId = "AA:BB:CC:DD:EE:FF"; // 替换为实际BLE设备MAC地址
                Console.WriteLine($"尝试连接到设备: {deviceId}");

                bool connected = await _bleTransport.ConnectAsync(deviceId);
                if (connected)
                {
                    Console.WriteLine("已成功连接到BLE设备");

                    // 等待连接建立完成
                    _connectionEvent.WaitOne();

                    // 发送数据到BLE设备
                    string message = "Hello from BLE client!";
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    Console.WriteLine($"发送数据: {message}");

                    int bytesSent = await _bleTransport.SendDataAsync(data);
                    Console.WriteLine($"已发送 {bytesSent} 字节数据");

                    // 等待接收数据（使用事件驱动方式）
                    Console.WriteLine("等待接收数据...");
                    _dataReceivedEvent.WaitOne(5000); // 等待5秒

                    // 或者使用同步接收方法
                    try
                    {
                        byte[] receivedData = await _bleTransport.ReceiveDataAsync(timeout: 3000);
                        string receivedMessage = Encoding.UTF8.GetString(receivedData);
                        Console.WriteLine($"同步接收数据: {receivedMessage}");
                    }
                    catch (TimeoutException)
                    {
                        Console.WriteLine("接收数据超时");
                    }

                    // 保持程序运行一段时间，观察可能的异步数据接收
                    Console.WriteLine("按任意键断开连接...");
                    Console.ReadKey();

                    // 断开连接
                    Console.WriteLine("正在断开连接...");
                    await _bleTransport.DisconnectAsync();
                    _disconnectionEvent.WaitOne(2000); // 等待断开完成
                    Console.WriteLine("已断开连接");
                }
                else
                {
                    Console.WriteLine("连接失败");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"内部错误: {ex.InnerException.Message}");
                }
            }
            finally
            {
                // 释放资源
                _bleTransport?.Dispose();
                _connectionEvent.Dispose();
                _disconnectionEvent.Dispose();
                _dataReceivedEvent.Dispose();

                Console.WriteLine("资源已释放，程序退出");
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
            else
            {
                _disconnectionEvent.Set();
            }
        }

        private static void OnDataReceived(object sender, DataReceivedEventArgs e)
        {
            try
            {
                string message = Encoding.UTF8.GetString(e.Data);
                Console.WriteLine($"接收到数据: {message} (来自设备: {e.DeviceId})");
                _dataReceivedEvent.Set();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理接收数据时发生错误: {ex.Message}");
            }
        }
    }
}
