using Server.Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Core.ProtocalService.UdpService
{
    public class UdpServiceInstance
    {
        private bool _isRunning;
        private ILogger _logger;
        private readonly int _udpport;
        // 用于 UdpClient 连接的监听器，负责监听 UDP 端口的客户端连接请求
        private UdpClient _udpListener;
        private uint _nextClientId;
        private ConnectionManager _ClientConnectionManager;

        public UdpServiceInstance(ref bool isRunning, ILogger logger, int udpport, ref uint nextClientId, ConnectionManager clientConnectionManager)
        {
            _isRunning = isRunning;
            _logger = logger;
            _udpport = udpport;
            _nextClientId = nextClientId;
            _ClientConnectionManager = clientConnectionManager;
        }

        public void Start()
        {
            _logger.LogDebug($"Starting to create an UDP listener for port {_udpport}.");
            _udpListener = new UdpClient(_udpport);
            _logger.LogDebug($"UDP listener for port {_udpport} has been created");

            _logger.LogDebug($"Starting to accept udp clients on port {_udpport}.");
            AcceptUdpClients();
            _logger.LogDebug("Accepting udp clients process has been initiated.");
        }

        public void Stop()
        {
            if (_udpListener != null)
            {
                _udpListener.Close();
                _logger.LogDebug("_udpListener has been stopped and disposed.");
            }
            else
            {
                _logger.LogTrace("The _udpListener listener is null, no disposal operation is required.");
            }

        }

        private async void AcceptUdpClients()
        {
            _logger.LogTrace("Enter AcceptUdpClients loop");

            while (_isRunning)
            {
                try
                {
                    var result = await _udpListener.ReceiveAsync();

                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _ClientConnectionManager.CreateClient(clientId).ConnectAsync();

                    var remoteEndPoint = result.RemoteEndPoint;
                    var data = result.Buffer;
                    _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
                    _logger.LogDebug($"Received UDP data from: {remoteEndPoint}");

                    // 处理 UDP 数据
                    _ = HandleUdpData(remoteEndPoint, data);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"UDP receive error: {ex.Message}");
                    _logger.LogWarning($"Retrying UDP receive in 100ms...");
                    await Task.Delay(100);
                }
            }

            _logger.LogTrace("Exited AcceptUdpClients loop (server stopped)");
        }

        private async Task HandleUdpData(IPEndPoint remoteEndPoint, byte[] data)
        {
            try
            {
                // 这里可以实现具体的 UDP 数据处理逻辑
                string message = System.Text.Encoding.UTF8.GetString(data);
                _logger.LogInformation($"Received UDP message from {remoteEndPoint}: {message}");

                // 示例：回显消息给客户端
                await _udpListener.SendAsync(data, data.Length, remoteEndPoint);
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Error handling UDP data: {ex.Message}, {ex}");
            }
        }
    }
}
