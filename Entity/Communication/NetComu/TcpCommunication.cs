using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Entity.Communication.NetComu.Common;

namespace Entity.Communication.NetComu
{
    // TCP通信实现
    public class TcpCommunication : NetComuBase
    {
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private Thread _receiveThread;

        public TcpCommunication(string host, int port) : base(host, port, NetType.TCP)
        {
        }

        protected override async Task ConnectAsync()
        {
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(IPAddress.Parse(Host), Port);
                _networkStream = _tcpClient.GetStream();

                // 设置超时
                _networkStream.ReadTimeout = ReceiveTimeout;
                _networkStream.WriteTimeout = SendTimeout;

                SetStatus(CommunicationStatus.Connected);

                // 启动接收线程
                _receiveThread = new Thread(ReceiveDataLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();
            }
            catch (Exception ex)
            {
                SetStatus(CommunicationStatus.Disconnected);
                throw new InvalidOperationException($"TCP连接失败: {ex.Message}", ex);
            }
        }

        protected override async Task DisconnectAsync()
        {
            try
            {
                _receiveThread?.Abort();
                _receiveThread = null;

                _networkStream?.Close();
                _networkStream = null;

                _tcpClient?.Close();
                _tcpClient = null;

                SetStatus(CommunicationStatus.Disconnected);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.ConnectionFailed, $"断开TCP连接失败: {ex.Message}", ex);
                throw;
            }
        }

        protected override async Task ProcessSendQueueAsync()
        {
            try
            {
                while (_sendQueue.TryDequeue(out byte[] data))
                {
                    if (_networkStream == null || !_tcpClient.Connected)
                    {
                        OnErrorOccurred(CommunicationErrorType.SendFailed, "发送失败：连接已断开");
                        break;
                    }

                    try
                    {
                        await _networkStream.WriteAsync(data, 0, data.Length);
                        await _networkStream.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(CommunicationErrorType.SendFailed, $"发送数据失败: {ex.Message}", ex);
                        // 将数据放回队列重试
                        _sendQueue.Enqueue(data);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.SendFailed, $"处理发送队列失败: {ex.Message}", ex);
            }
        }

        private void ReceiveDataLoop()
        {
            try
            {
                byte[] buffer = new byte[ReceiveBufferSize];

                while (!_cancellationTokenSource.IsCancellationRequested && _tcpClient != null && _tcpClient.Connected)
                {
                    try
                    {
                        if (!_networkStream.DataAvailable)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        int bytesRead = _networkStream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            byte[] data = new byte[bytesRead];
                            Array.Copy(buffer, 0, data, 0, bytesRead);
                            ProcessReceivedData(data);
                        }
                        else
                        {
                            // 连接已关闭
                            throw new IOException("连接已关闭");
                        }
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode == SocketError.TimedOut)
                        {
                            // 超时，继续等待
                            continue;
                        }

                        OnErrorOccurred(CommunicationErrorType.ReceiveFailed, $"接收数据失败: {ex.Message}", ex);
                        break;
                    }
                    catch (IOException ex)
                    {
                        OnErrorOccurred(CommunicationErrorType.ReceiveFailed, $"接收数据失败: {ex.Message}", ex);
                        break;
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(CommunicationErrorType.ReceiveFailed, $"接收数据时发生未知错误: {ex.Message}", ex);
                        break;
                    }
                }
            }
            catch (ThreadAbortException)
            {
                // 线程被中止，正常关闭
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.ReceiveFailed, $"接收线程异常: {ex.Message}", ex);
            }
            finally
            {
                // 确保连接关闭
                CloseAsync().Wait();
            }
        }

        protected override Task ReceiveDataLoopAsync()
        {
            // 已在ReceiveDataLoop中实现
            return Task.CompletedTask;
        }
    }

    // 使用示例
    public class TcpProtocolExample
    {
        public static async Task Main(string[] args)
        {
            // TCP通信示例
            using (TcpCommunication tcpComm = new TcpCommunication("192.168.1.100", 502))
            {
                tcpComm.DataReceived += (sender, data) =>
                {
                    Console.WriteLine($"收到TCP数据: {BitConverter.ToString(data)}");
                };

                tcpComm.ErrorOccurred += (sender, e) =>
                {
                    Console.WriteLine($"TCP通信错误: {e.Message}");
                };

                await tcpComm.OpenAsync();
                Console.WriteLine("TCP连接已打开");

                // 发送数据
                await tcpComm.SendAsync(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x01, 0x84, 0x0A });
            }
        }
    }
}
