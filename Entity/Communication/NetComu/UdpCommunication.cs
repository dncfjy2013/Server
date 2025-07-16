using Entity.Communication.NetComu.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Entity.Communication.NetComu
{
    // UDP通信实现
    public class UdpCommunication : NetComuBase
    {
        private UdpClient _udpClient;
        private IPEndPoint _remoteEndpoint;
        private Thread _receiveThread;

        public UdpCommunication(string host, int port) : base(host, port, NetType.UDP)
        {
            _remoteEndpoint = new IPEndPoint(IPAddress.Parse(host), port);
        }

        protected override async Task ConnectAsync()
        {
            try
            {
                _udpClient = new UdpClient();
                _udpClient.Connect(_remoteEndpoint);

                SetStatus(CommunicationStatus.Connected);

                // 启动接收线程
                _receiveThread = new Thread(ReceiveDataLoop);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();
            }
            catch (Exception ex)
            {
                SetStatus(CommunicationStatus.Disconnected);
                throw new InvalidOperationException($"UDP连接失败: {ex.Message}", ex);
            }
        }

        protected override async Task DisconnectAsync()
        {
            try
            {
                _receiveThread?.Abort();
                _receiveThread = null;

                _udpClient?.Close();
                _udpClient = null;

                SetStatus(CommunicationStatus.Disconnected);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(CommunicationErrorType.ConnectionFailed, $"断开UDP连接失败: {ex.Message}", ex);
                throw;
            }
        }

        protected override async Task ProcessSendQueueAsync()
        {
            try
            {
                while (_sendQueue.TryDequeue(out byte[] data))
                {
                    if (_udpClient == null)
                    {
                        OnErrorOccurred(CommunicationErrorType.SendFailed, "发送失败：连接已断开");
                        break;
                    }

                    try
                    {
                        await _udpClient.SendAsync(data, data.Length);
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
                IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);

                while (!_cancellationTokenSource.IsCancellationRequested && _udpClient != null)
                {
                    try
                    {
                        if (_udpClient.Available > 0)
                        {
                            byte[] data = _udpClient.Receive(ref remoteEndpoint);
                            if (data.Length > 0)
                            {
                                ProcessReceivedData(data);
                            }
                        }
                        else
                        {
                            Thread.Sleep(10);
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
    public class UdpProtocolExample
    {
        public static async Task Main(string[] args)
        {
            // UDP通信示例
            using (UdpCommunication udpComm = new UdpCommunication("192.168.1.100", 5000))
            {
                udpComm.DataReceived += (sender, data) =>
                {
                    Console.WriteLine($"收到UDP数据: {BitConverter.ToString(data)}");
                };

                udpComm.ErrorOccurred += (sender, e) =>
                {
                    Console.WriteLine($"UDP通信错误: {e.Message}");
                };

                await udpComm.OpenAsync();
                Console.WriteLine("UDP连接已打开");

                // 发送数据
                await udpComm.SendAsync(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            }
        }
    }
}
