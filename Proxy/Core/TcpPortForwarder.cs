using Server.Logger;
using Server.Proxy.Common;
using Server.Proxy.Config;
using Server.Proxy.LoadBalance;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    /// <summary>
    /// TCP/SSL-TCP转发器，负责处理TCP协议的端口转发、连接池管理和负载均衡
    /// </summary>
    public sealed class TcpPortForwarder : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly ILoadBalancer _loadBalancer;
        private readonly ConcurrentDictionary<int, TcpListener> _tcpListeners = new();
        private readonly ConcurrentDictionary<string, Stack<TcpClient>> _connectionPools = new();
        private readonly ConcurrentDictionary<int, RateLimiter> _portLimiters = new();
        private readonly ConcurrentDictionary<string, ConnectionMetrics> _connectionMetrics = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _isRunning;
        private bool _disposed;
        private const int MaxPooledConnections = 50;

        public TcpPortForwarder(ILogger logger, ILoadBalancer loadBalancer)
        {
            _logger = logger;
            _loadBalancer = loadBalancer;
        }

        public void Init(IEnumerable<EndpointConfig> endpoints)
        {
            // 初始化TCP/SslTcp端点的限流器
            foreach (var ep in endpoints.Where(e =>
                     e.Protocol == ConnectType.Tcp ||
                     e.Protocol == ConnectType.SslTcp))
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
            if (_isRunning) throw new InvalidOperationException("TCP转发器已运行");
            _isRunning = true;
            _logger.LogInformation("启动TCP端口转发器...");

            var tasks = new List<Task>();
            foreach (var ep in endpoints.Where(e =>
                     e.Protocol == ConnectType.Tcp ||
                     e.Protocol == ConnectType.SslTcp))
            {
                tasks.Add(RunTcpEndpointAsync(ep, _cancellationTokenSource.Token));
            }

            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { _logger.LogInformation("TCP转发器已停止"); }
            catch (Exception ex) { _logger.LogCritical($"TCP转发器启动失败：{ex.Message}"); }
            finally { _isRunning = false; }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            _logger.LogInformation("开始停止TCP转发器...");
            _cancellationTokenSource.Cancel();

            // 停止监听器
            var stopTasks = new List<Task>();
            foreach (var listener in _tcpListeners.Values)
            {
                stopTasks.Add(Task.Run(() => listener.Stop()));
            }
            await Task.WhenAll(stopTasks);
            _tcpListeners.Clear();

            // 释放连接池
            foreach (var pool in _connectionPools.Values)
            {
                foreach (var client in pool) client.Dispose();
            }
            _connectionPools.Clear();

            // 释放限流器
            foreach (var limiter in _portLimiters.Values) limiter.Dispose();
            _portLimiters.Clear();

            _logger.LogInformation("TCP转发器已完全停止");
        }

        public PortForwarderMetrics GetMetrics()
        {
            return new PortForwarderMetrics
            {
                ActiveConnections = _connectionMetrics.Values.Sum(m => m.ActiveConnections),
                ConnectionMetrics = _connectionMetrics.Values.ToList(),
                EndpointStatus = _tcpListeners.Keys.Select(port => new EndpointStatus
                {
                    ListenPort = port,
                    Protocol = ConnectType.Tcp,
                    IsActive = _tcpListeners.ContainsKey(port)
                }).ToList()
            };
        }

        private async Task RunTcpEndpointAsync(EndpointConfig ep, CancellationToken ct)
        {
            var listener = new TcpListener(IPAddress.Parse(ep.ListenIp), ep.ListenPort);
            listener.Start();

            if (!_tcpListeners.TryAdd(ep.ListenPort, listener))
            {
                listener.Stop();
                throw new InvalidOperationException($"端口 {ep.ListenPort} 已被占用");
            }

            _logger.LogInformation($"TCP监听器启动：端口 {ep.ListenPort}，协议 {ep.Protocol}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var lease = await _portLimiters[ep.ListenPort].AcquireAsync(1, ct);
                    if (!lease.IsAcquired)
                    {
                        _logger.LogWarning($"端口 {ep.ListenPort} 连接数已满，拒绝新连接");
                        continue;
                    }

                    var client = await listener.AcceptTcpClientAsync(ct);
                    _ = HandleTcpConnectionAsync(client, ep, ct);
                }
            }
            finally
            {
                _tcpListeners.TryRemove(ep.ListenPort, out _);
                listener.Stop();
                _logger.LogInformation($"TCP监听器停止：端口 {ep.ListenPort}");
            }
        }

        private async Task HandleTcpConnectionAsync(TcpClient client, EndpointConfig ep, CancellationToken ct)
        {
            var connectionId = Guid.NewGuid().ToString();
            var stopwatch = Stopwatch.StartNew();
            TargetServer target = null;
            TcpClient targetClient = null;
            try
            {
                // 负载均衡选择服务器
                target = await _loadBalancer.SelectServerAsync(ep, client.Client.RemoteEndPoint);
                UpdateMetrics(target, 1);
                _logger.LogDebug($"新连接 [{connectionId}]：{client.Client.RemoteEndPoint} → {target.Ip}:{target.TargetPort}");

                // 处理客户端SSL
                var clientStream = await ProcessClientSslAsync(client, ep, ct);
                // 获取目标连接
                targetClient = await GetOrCreateTargetConnectionAsync(target, ct);
                // 处理目标服务器SSL
                var targetStream = await ProcessTargetSslAsync(targetClient, target, ct);

                // 双向转发
                var forwardTask = ForwardStreamsAsync(clientStream, targetStream, connectionId, ct);
                var reverseTask = ForwardStreamsAsync(targetStream, clientStream, connectionId, ct, true);
                await Task.WhenAny(forwardTask, reverseTask);
            }
            catch (Exception ex)
            {
                _logger.LogError($"连接 [{connectionId}] 处理失败：{ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
                await ReleaseResourcesAsync(client, targetClient, target, connectionId, stopwatch);
            }
        }

        private async Task<Stream> ProcessClientSslAsync(TcpClient client, EndpointConfig ep, CancellationToken ct)
        {
            if (ep.Protocol == ConnectType.SslTcp)
            {
                var sslStream = new SslStream(client.GetStream(), false, ValidateClientCertificate);
                await sslStream.AuthenticateAsServerAsync(
                    ep.ServerCertificate,
                    ep.ClientCertificateRequired,
                    SslProtocols.Tls12 | SslProtocols.Tls13,
                    false);
                return sslStream;
            }
            return client.GetStream();
        }

        private async Task<TcpClient> GetOrCreateTargetConnectionAsync(TargetServer target, CancellationToken ct)
        {
            var poolKey = $"{target.Ip}:{target.TargetPort}";
            if (!_connectionPools.TryGetValue(poolKey, out var pool))
            {
                pool = new Stack<TcpClient>();
                _connectionPools[poolKey] = pool;
            }

            TcpClient targetClient;
            if (pool.TryPop(out targetClient) && targetClient.Connected)
            {
                _logger.LogTrace($"从连接池获取连接：{poolKey}");
            }
            else
            {
                targetClient = new TcpClient();
                await targetClient.ConnectAsync(target.Ip, target.TargetPort, ct);
            }
            return targetClient;
        }

        private async Task<Stream> ProcessTargetSslAsync(TcpClient targetClient, TargetServer target, CancellationToken ct)
        {
            if (target.BackendProtocol == ConnectType.SslTcp)
            {
                var sslStream = new SslStream(targetClient.GetStream(), false);
                await sslStream.AuthenticateAsClientAsync(target.Ip);
                return sslStream;
            }
            return targetClient.GetStream();
        }

        private async Task ForwardStreamsAsync(Stream source, Stream destination, string connectionId,
            CancellationToken ct, bool isReverse = false)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, bytesRead, ct);
                    await destination.FlushAsync(ct);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }

        private async Task ReleaseResourcesAsync(TcpClient client, TcpClient targetClient, TargetServer target,
            string connectionId, Stopwatch stopwatch)
        {
            if (targetClient != null && target != null)
            {
                var poolKey = $"{target.Ip}:{target.TargetPort}";
                if (_connectionPools.TryGetValue(poolKey, out var pool) &&
                    targetClient.Connected &&
                    pool.Count < MaxPooledConnections)
                {
                    pool.Push(targetClient);
                    _logger.LogTrace($"连接 [{connectionId}] 回收至连接池：{poolKey}");
                }
                else { targetClient?.Dispose(); }
            }

            client?.Dispose();
            if (target != null)
            {
                UpdateMetrics(target, -1);
                _logger.LogInformation($"连接 [{connectionId}] 关闭：{stopwatch.ElapsedMilliseconds}ms，目标：{target.Ip}:{target.TargetPort}");
            }
        }

        private bool ValidateClientCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None) return true;
            _logger.LogWarning($"客户端证书验证失败: {errors}");
            return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        }

        private void UpdateMetrics(TargetServer target, int delta)
        {
            var key = $"{target.Ip}:{target.TargetPort}";
            target.Increment(delta); // 假设TargetServer包含原子操作
            _connectionMetrics.AddOrUpdate(key,
                _ => new ConnectionMetrics
                {
                    Target = key,
                    ActiveConnections = delta,
                    TotalConnections = delta > 0 ? 1 : 0,
                    LastActivity = DateTime.UtcNow
                },
                (_, m) => {
                    m.ActiveConnections += delta;
                    if (delta > 0) m.TotalConnections++;
                    m.LastActivity = DateTime.UtcNow;
                    return m;
                });
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await StopAsync(TimeSpan.FromSeconds(30));
                _cancellationTokenSource.Dispose();
                _tcpListeners.Clear();
                _connectionPools.Clear();
                _portLimiters.Clear();
            }
        }
    }
}