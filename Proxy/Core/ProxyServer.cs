using Server.Logger;
using Server.Proxy.Config;
using Server.Proxy.LoadBalance;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    sealed class AdvancedPortForwarder : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private List<EndpointConfig> _endpoints;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _isRunning;
        private bool _disposed;

        // 协议专属转发器
        private readonly TcpPortForwarder _tcpForwarder;
        private readonly UdpPortForwarder _udpForwarder;
        private readonly HttpPortForwarder _httpForwarder;

        public AdvancedPortForwarder(ILogger logger, ILoadBalancer loadBalancer)
        {
            _logger = logger;
            _tcpForwarder = new TcpPortForwarder(logger, loadBalancer);
            _udpForwarder = new UdpPortForwarder(logger);
            _httpForwarder = new HttpPortForwarder(logger, loadBalancer);
            InitIpZone(); // 如有全局IP策略可在此初始化
        }

        public void Init(IEnumerable<EndpointConfig> endpoints)
        {
            _endpoints = endpoints.ToList();
            _tcpForwarder.Init(endpoints);
            _udpForwarder.Init(endpoints);
            _httpForwarder.Init(endpoints);
        }

        public async Task StartAsync()
        {
            if (_isRunning) throw new InvalidOperationException("转发器已运行");
            _isRunning = true;
            _logger.LogInformation("启动高级端口转发器...");

            var tasks = new List<Task>
            {
                _tcpForwarder.StartAsync(_endpoints),
                _udpForwarder.StartAsync(_endpoints),
                _httpForwarder.StartAsync(_endpoints)
            };

            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { _logger.LogInformation("转发器停止请求已接收"); }
            finally { _isRunning = false; }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            _logger.LogInformation("开始优雅停止转发器...");
            _cancellationTokenSource.Cancel();

            await Task.WhenAll(
                _tcpForwarder.StopAsync(timeout),
                _udpForwarder.StopAsync(timeout),
                _httpForwarder.StopAsync(timeout)
            );

            _logger.LogInformation("所有协议转发器已停止");
        }

        public PortForwarderMetrics GetMetrics()
        {
            var tcpMetrics = _tcpForwarder.GetMetrics();
            var udpMetrics = _udpForwarder.GetMetrics();
            var httpMetrics = _httpForwarder.GetMetrics();

            return new PortForwarderMetrics
            {
                ActiveConnections = tcpMetrics.ActiveConnections + udpMetrics.ActiveConnections + httpMetrics.ActiveConnections,
                ConnectionMetrics = tcpMetrics.ConnectionMetrics
                    .Concat(udpMetrics.ConnectionMetrics)
                    .Concat(httpMetrics.ConnectionMetrics)
                    .ToList(),
                EndpointStatus = tcpMetrics.EndpointStatus
                    .Concat(udpMetrics.EndpointStatus)
                    .Concat(httpMetrics.EndpointStatus)
                    .ToList()
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await StopAsync(TimeSpan.FromSeconds(30));
                _cancellationTokenSource.Dispose();
                await _tcpForwarder.DisposeAsync();
                await _udpForwarder.DisposeAsync();
                await _httpForwarder.DisposeAsync();
            }
        }

        private void InitIpZone()
        {
            // 如有全局IP策略初始化逻辑可放置此处
        }
    }
}