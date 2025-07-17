using Entity.Communication.WiFiComu.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.WiFiComu
{
    internal class SocketWiFiCommunication : WiFiComuBase
    {
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private Task _receiveTask;
        private CancellationTokenSource _receiveCts;

        protected override async Task<bool> ConnectCoreAsync(WiFiConnectionParameters parameters, CancellationToken cancellationToken)
        {
            // 在实际实现中，这里需要使用系统API连接到WiFi网络
            // 此处仅为示例，实际实现可能需要使用Windows.Networking.Connectivity或其他平台特定API

            // 模拟连接过程
            await Task.Delay(1000, cancellationToken);

            // 创建TCP客户端
            _tcpClient = new TcpClient();

            // 尝试连接到指定的服务器（示例使用本地回环地址）
            try
            {
                string serverIp = parameters.AdditionalParameters.GetValueOrDefault("ServerIP", "127.0.0.1");
                int serverPort = int.TryParse(parameters.AdditionalParameters.GetValueOrDefault("ServerPort", "8080"), out int port) ? port : 8080;

                await _tcpClient.ConnectAsync(IPAddress.Parse(serverIp), serverPort, cancellationToken);
                _networkStream = _tcpClient.GetStream();

                // 启动数据接收任务
                StartReceivingData();

                return true;
            }
            catch
            {
                _tcpClient?.Dispose();
                _tcpClient = null;
                throw;
            }
        }

        protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            // 停止接收任务
            _receiveCts?.Cancel();
            await (_receiveTask ?? Task.CompletedTask);

            // 关闭网络流和客户端
            _networkStream?.Dispose();
            _networkStream = null;

            _tcpClient?.Dispose();
            _tcpClient = null;
        }

        protected override async Task<int> SendDataCoreAsync(string targetIP, int targetPort, byte[] data, CancellationToken cancellationToken)
        {
            if (_networkStream == null || !_tcpClient.Connected)
                throw new InvalidOperationException("网络连接已断开");

            await _networkStream.WriteAsync(data, 0, data.Length, cancellationToken);
            return data.Length;
        }

        protected override async Task<(byte[] Data, string SourceIP, int SourcePort)> ReceiveDataCoreAsync(CancellationToken cancellationToken)
        {
            if (_networkStream == null || !_tcpClient.Connected)
                throw new InvalidOperationException("网络连接已断开");

            // 注意：实际应用中可能需要更复杂的接收逻辑，处理数据包边界等问题
            byte[] buffer = new byte[4096];
            int bytesRead = await _networkStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

            if (bytesRead == 0)
                throw new IOException("连接已关闭");

            byte[] data = new byte[bytesRead];
            Array.Copy(buffer, data, bytesRead);

            // 对于TCP连接，源IP和端口可以从TcpClient获取
            var clientEndpoint = _tcpClient.Client.RemoteEndPoint as IPEndPoint;
            string sourceIp = clientEndpoint?.Address.ToString() ?? "unknown";
            int sourcePort = clientEndpoint?.Port ?? 0;

            return (data, sourceIp, sourcePort);
        }

        private void StartReceivingData()
        {
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);

            _receiveTask = Task.Run(async () =>
            {
                try
                {
                    while (!_receiveCts.Token.IsCancellationRequested && _networkStream != null)
                    {
                        try
                        {
                            var (data, sourceIp, sourcePort) = await ReceiveDataCoreAsync(_receiveCts.Token);
                            OnDataReceived(data, sourceIp, sourcePort);
                        }
                        catch (OperationCanceledException)
                        {
                            // 正常取消，忽略
                        }
                        catch (Exception ex)
                        {
                            if (!_receiveCts.Token.IsCancellationRequested)
                            {
                                Console.WriteLine($"接收数据时发生错误: {ex.Message}");
                                UpdateConnectionState(WiFiConnectionState.Error, "数据接收错误", ex);

                                // 发生错误时断开连接
                                await DisconnectAsync();
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    _receiveCts.Dispose();
                }
            });
        }
    }

    // 使用示例
    public class SocketWiFiTest
    {
        private static IWiFiTransport _wifiTransport;
        private static readonly ManualResetEvent _connectionEvent = new ManualResetEvent(false);
        private static readonly ManualResetEvent _dataReceivedEvent = new ManualResetEvent(false);

        public static async Task Main()
        {
            try
            {
                Console.WriteLine("WiFi通信示例程序启动...");

                // 创建WiFi传输实例
                _wifiTransport = new SocketWiFiCommunication();

                // 注册事件处理程序
                _wifiTransport.ConnectionStateChanged += OnConnectionStateChanged;
                _wifiTransport.DataReceived += OnDataReceived;

                // 准备连接参数
                var connectionParams = new WiFiConnectionParameters
                {
                    SSID = "YourWiFiNetwork",
                    Password = "YourWiFiPassword",
                    AdditionalParameters =
                {
                    { "ServerIP", "192.168.1.100" },  // 替换为实际服务器IP
                    { "ServerPort", "8080" }           // 替换为实际服务器端口
                }
                };

                // 连接到WiFi网络
                Console.WriteLine($"正在连接到WiFi网络: {connectionParams.SSID}");
                bool connected = await _wifiTransport.ConnectAsync(connectionParams);

                if (connected)
                {
                    Console.WriteLine("已成功连接到WiFi网络");

                    // 等待连接建立完成
                    _connectionEvent.WaitOne();

                    // 发送数据
                    string message = "Hello, WiFi World!";
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    Console.WriteLine($"正在发送数据: {message}");

                    int bytesSent = await _wifiTransport.SendDataAsync(
                        connectionParams.AdditionalParameters["ServerIP"],
                        int.Parse(connectionParams.AdditionalParameters["ServerPort"]),
                        data);

                    Console.WriteLine($"已发送 {bytesSent} 字节数据");

                    // 接收数据
                    Console.WriteLine("等待接收数据...");
                    try
                    {
                        var (receivedData, sourceIP, sourcePort) = await _wifiTransport.ReceiveDataAsync(timeout: 5000);
                        string receivedMessage = Encoding.UTF8.GetString(receivedData);
                        Console.WriteLine($"接收到来自 {sourceIP}:{sourcePort} 的数据: {receivedMessage}");
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
                if (_wifiTransport != null)
                {
                    Console.WriteLine("正在断开连接...");
                    await _wifiTransport.DisconnectAsync();
                    _wifiTransport.Dispose();
                    Console.WriteLine("已断开连接并释放资源");
                }

                // 清理事件
                _connectionEvent.Dispose();
                _dataReceivedEvent.Dispose();
            }
        }

        private static void OnConnectionStateChanged(object sender, WiFiConnectionStateChangedEventArgs e)
        {
            Console.WriteLine($"WiFi连接状态变更: {e.NewState}");
            if (!string.IsNullOrEmpty(e.Message))
            {
                Console.WriteLine($"消息: {e.Message}");
            }
            if (e.Error != null)
            {
                Console.WriteLine($"错误: {e.Error.Message}");
            }

            if (e.NewState == WiFiConnectionState.Connected)
            {
                _connectionEvent.Set();
            }
        }

        private static void OnDataReceived(object sender, WiFiDataReceivedEventArgs e)
        {
            try
            {
                string message = Encoding.UTF8.GetString(e.Data);
                Console.WriteLine($"接收到数据: {message} (来自 {e.SourceIP}:{e.SourcePort})");
                _dataReceivedEvent.Set();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理接收数据时发生错误: {ex.Message}");
            }
        }
    }
}
