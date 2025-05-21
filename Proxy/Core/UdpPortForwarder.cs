using Proxy.Common;
using Server.Logger;
using Server.Proxy.Common;
using Server.Proxy.Config;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    /// <summary>
    /// UDP转发器实现，负责处理UDP协议的端口转发
    /// 功能特性：
    /// ✅ 支持UDP协议的数据包转发
    /// ✅ 基于限流器的资源控制
    /// ✅ 负载均衡支持
    /// ✅ 连接指标统计
    /// ✅ 优雅停止机制
    /// </summary>
    public sealed class UdpPortForwarder : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<int, UdpClient> _udpClients = new();
        private readonly ConcurrentDictionary<string, TargetServer> _udpMapServer = new();
        private readonly ConcurrentDictionary<int, RateLimiter> _portLimiters = new();
        private readonly ConcurrentDictionary<string, ConnectionMetrics> _connectionMetrics = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _isRunning;
        private bool _disposed;
        private readonly ILoadBalancer _loadBalancer;
        public UdpPortForwarder(ILogger logger, ILoadBalancer loadBalancer)
        {
            _logger = logger;
            _loadBalancer = loadBalancer;
        }

        public void Init(IEnumerable<EndpointConfig> endpoints)
        {
            foreach (var ep in endpoints.Where(e => e.Protocol == ConnectType.Udp))
            {
                _portLimiters[ep.ListenPort] = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = ep.MaxConnections,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 100
                });
            }
        }

        public async Task StartAsync(IEnumerable<EndpointConfig> endpoints)
        {
            if (_isRunning)
                throw new InvalidOperationException("UDP转发器已处于运行状态");

            _isRunning = true;
            _logger.LogInformation("启动UDP端口转发器...");

            var tasks = new List<Task>();
            foreach (var ep in endpoints.Where(e => e.Protocol == ConnectType.Udp))
            {
                tasks.Add(RunUdpEndpointAsync(ep, _cancellationTokenSource.Token));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("UDP转发器已停止");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"UDP转发器启动失败：{ex.Message}");
            }
            finally
            {
                _isRunning = false;
            }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            _logger.LogInformation("开始停止UDP转发器...");
            _cancellationTokenSource.Cancel();

            var tasks = new List<Task>();
            foreach (var client in _udpClients.Values)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        client.Close();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"停止UDP客户端失败：{ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
            _udpClients.Clear();

            foreach (var limiter in _portLimiters.Values)
            {
                limiter.Dispose();
            }
            _portLimiters.Clear();

            _logger.LogInformation("UDP转发器已完全停止");
        }

        public PortForwarderMetrics GetMetrics()
        {
            return new PortForwarderMetrics
            {
                ActiveConnections = _connectionMetrics.Values.Sum(m => m.ActiveConnections),
                ConnectionMetrics = _connectionMetrics.Values.ToList(),
                EndpointStatus = _udpClients.Keys.Select(port => new EndpointStatus
                {
                    ListenPort = port,
                    Protocol = ConnectType.Udp,
                    IsActive = _udpClients.ContainsKey(port)
                }).ToList()
            };
        }

        private async Task RunUdpEndpointAsync(EndpointConfig ep, CancellationToken ct)
        {
            var udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(ep.ListenIp), ep.ListenPort));

            if (!_udpClients.TryAdd(ep.ListenPort, udpClient))
            {
                udpClient.Close();
                throw new InvalidOperationException($"UDP端口 {ep.ListenPort} 已被占用");
            }

            _logger.LogInformation($"UDP监听器启动：端口 {ep.ListenPort}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var lease = await _portLimiters[ep.ListenPort].AcquireAsync(1, ct);
                    if (!lease.IsAcquired)
                    {
                        _logger.LogWarning($"UDP端口 {ep.ListenPort} 处理队列已满，丢弃数据包");
                        await Task.Delay(100, ct);
                        continue;
                    }

                    try
                    {
                        var result = await udpClient.ReceiveAsync(ct);
                        _ = HandleUdpPacketAsync(result, ep, ct);
                    }
                    catch (SocketException ex)
                    {
                        _logger.LogWarning($"UDP接收失败：{ex.SocketErrorCode} - {ex.Message}");
                    }
                }
            }
            finally
            {
                _udpClients.TryRemove(ep.ListenPort, out _);
                udpClient.Close();
                _logger.LogInformation($"UDP监听器停止：端口 {ep.ListenPort}");
            }
        }

        private async Task HandleUdpPacketAsync(UdpReceiveResult result, EndpointConfig ep, CancellationToken ct)
        {
            try
            {
                var clientKey = result.RemoteEndPoint.ToString();
                TargetServer target;
                if (ProxyConstant._isUseUDPMap)
                {
                    target = _udpMapServer.GetOrAdd(clientKey, _ => SelectServerAsync(ep).GetAwaiter().GetResult());
                }
                else
                {
                    target = await SelectServerAsync(ep);
                }

                using var client = new UdpClient();
                var targetEndpoint = new IPEndPoint(IPAddress.Parse(target.Ip), target.TargetPort);
                await client.SendAsync(result.Buffer, result.Buffer.Length, targetEndpoint);

                _logger.LogDebug($"UDP转发完成：{result.RemoteEndPoint} → {target.Ip}:{target.TargetPort}，字节数 {result.Buffer.Length}");
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("UDP转发被取消");
            }
            catch (Exception ex)
            {
                _logger.LogError($"UDP转发失败：{ex.Message}");
            }
        }

        private async Task<TargetServer> SelectServerAsync(EndpointConfig ep)
        {
            return _loadBalancer.SelectServerAsync(ep).GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;

                try
                {
                    if (_isRunning)
                    {
                        await StopAsync(TimeSpan.FromSeconds(30));
                    }
                }
                finally
                {
                    _cancellationTokenSource.Dispose();

                    foreach (var client in _udpClients.Values)
                    {
                        client.Close();
                    }
                    _udpClients.Clear();

                    foreach (var limiter in _portLimiters.Values)
                    {
                        limiter.Dispose();
                    }
                    _portLimiters.Clear();
                }
            }
        }
    }
}