using Proxy.Common;
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
using System.Text;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    /// <summary>
    /// 高性能TCP/SSL-TCP转发器，负责处理TCP协议的端口转发、连接池管理和负载均衡
    /// </summary>
    public sealed class TcpPortForwarder : IAsyncDisposable
    {
        // 使用更高效的对象池替代Stack，避免锁竞争
        private readonly ILogger _logger;
        private readonly ILoadBalancer _loadBalancer;
        private readonly ConcurrentDictionary<int, TcpListener> _tcpListeners = new();
        private readonly ConcurrentDictionary<string, ObjectPool<TcpClient>> _connectionPools = new();
        private readonly ConcurrentDictionary<int, RateLimiter> _portLimiters = new();
        private readonly ConcurrentDictionary<string, ConnectionMetrics> _connectionMetrics = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ConcurrentDictionary<string, TargetServer> _tcpMapServer = new();
        private readonly ConcurrentDictionary<string, X509Certificate2> _certificateCache = new();
        private bool _isRunning;
        private bool _disposed;
        private const int MaxPooledConnections = 50;
        private const int BufferSize = 65536; // 增大缓冲区大小以提高吞吐量
        private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan IdleConnectionTimeout = TimeSpan.FromMinutes(2);

        public TcpPortForwarder(ILogger logger, ILoadBalancer loadBalancer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _loadBalancer = loadBalancer ?? throw new ArgumentNullException(nameof(loadBalancer));
        }

        public void Init(IEnumerable<EndpointConfig> endpoints)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));

            // 初始化TCP/SslTcp端点的限流器
            foreach (var ep in endpoints.Where(e =>
                     e.Protocol == ConnectType.Tcp ||
                     e.Protocol == ConnectType.SslTcp))
            {
                _portLimiters[ep.ListenPort] = CreateRateLimiter(ep);
            }
        }

        private RateLimiter CreateRateLimiter(EndpointConfig ep)
        {
            return new ConcurrencyLimiter(new ConcurrencyLimiterOptions
            {
                PermitLimit = ep.MaxConnections,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 100
            });
        }

        public async Task StartAsync(IEnumerable<EndpointConfig> endpoints)
        {
            if (_isRunning) throw new InvalidOperationException("TCP转发器已运行");
            _isRunning = true;
            _logger.LogInformation("启动高性能TCP端口转发器...");

            var tasks = new List<Task>();
            foreach (var ep in endpoints.Where(e =>
                     e.Protocol == ConnectType.Tcp ||
                     e.Protocol == ConnectType.SslTcp))
            {
                tasks.Add(RunTcpEndpointAsync(ep, _cancellationTokenSource.Token));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("TCP转发器已停止");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"TCP转发器启动失败：{ex.Message}", ex);
            }
            finally
            {
                _isRunning = false;
            }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            _logger.LogInformation("开始停止TCP转发器...");
            _cancellationTokenSource.Cancel();

            using var timeoutCts = new CancellationTokenSource(timeout);
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token, timeoutCts.Token);

            try
            {
                // 停止监听器
                var stopTasks = new List<Task>();
                foreach (var listener in _tcpListeners.Values)
                {
                    stopTasks.Add(Task.Run(() => listener.Stop(), linkedCts.Token));
                }
                await Task.WhenAll(stopTasks);
                _tcpListeners.Clear();

                // 释放连接池
                foreach (var pool in _connectionPools.Values)
                {
                    pool.Dispose();
                }
                _connectionPools.Clear();

                // 释放限流器
                foreach (var limiter in _portLimiters.Values)
                {
                    limiter.Dispose();
                }
                _portLimiters.Clear();

                _certificateCache.Clear();

                _logger.LogInformation("TCP转发器已完全停止");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("停止操作超时，部分资源可能未完全释放");
            }
            catch (Exception ex)
            {
                _logger.LogError($"停止过程中发生错误: {ex.Message}", ex);
            }
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
            if (ep == null) throw new ArgumentNullException(nameof(ep));

            var listener = new TcpListener(IPAddress.Parse(ep.ListenIp), ep.ListenPort);
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                _logger.LogCritical($"无法在端口 {ep.ListenPort} 启动TCP监听器: {ex.Message}", ex);
                throw;
            }

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
                    using var timeoutCts = new CancellationTokenSource(ConnectionTimeout);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                    try
                    {
                        using var lease = await _portLimiters[ep.ListenPort].AcquireAsync(1, linkedCts.Token);
                        if (!lease.IsAcquired)
                        {
                            _logger.LogWarning($"端口 {ep.ListenPort} 连接数已满，拒绝新连接");
                            continue;
                        }

                        var client = await listener.AcceptTcpClientAsync(linkedCts.Token);
                        ConfigureTcpClient(client);

                        // 使用I/O密集型线程执行连接处理
                        _ = Task.Run(() => HandleTcpConnectionAsync(client, ep, ct), ct);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        _logger.LogDebug($"接受客户端连接超时");
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        _logger.LogError($"接受客户端连接时发生错误: {ex.Message}", ex);
                    }
                }
            }
            finally
            {
                _tcpListeners.TryRemove(ep.ListenPort, out _);
                listener.Stop();
                _logger.LogInformation($"TCP监听器停止：端口 {ep.ListenPort}");
            }
        }

        private void ConfigureTcpClient(TcpClient client)
        {
            try
            {
                client.NoDelay = true; // 禁用Nagle算法以减少延迟
                client.ReceiveBufferSize = BufferSize;
                client.SendBufferSize = BufferSize;
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 300);
                client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 60);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"配置TCP客户端选项失败: {ex.Message}");
            }
        }

        private async Task HandleTcpConnectionAsync(TcpClient client, EndpointConfig ep, CancellationToken ct)
        {
            var connectionId = Guid.NewGuid().ToString();
            var stopwatch = ValueStopwatch.StartNew();
            TargetServer target = null;
            TcpClient targetClient = null;
            Stream clientStream = null;
            Stream targetStream = null;

            try
            {
                // 负载均衡选择服务器
                if (ProxyConstant._isUseTCPMap)
                {
                    target = _tcpMapServer.GetOrAdd(
                        client.Client.RemoteEndPoint.ToString(),
                        _ => _loadBalancer.SelectServerAsync(ep).GetAwaiter().GetResult());
                }
                else
                {
                    target = await _loadBalancer.SelectServerAsync(ep);
                }

                UpdateMetrics(target, 1);
                _logger.LogDebug($"新连接 [{connectionId}]：{client.Client.RemoteEndPoint} → {target.Ip}:{target.TargetPort}");

                // 处理客户端SSL
                clientStream = await ProcessClientSslAsync(client, ep, ct);

                // 获取目标连接
                targetClient = await GetOrCreateTargetConnectionAsync(target, ct);
                ConfigureTcpClient(targetClient);

                // 处理目标服务器SSL
                targetStream = await ProcessTargetSslAsync(targetClient, target, ct);

                // 双向转发使用高性能管道
                await ForwardStreamsBidirectionalAsync(clientStream, targetStream, connectionId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug($"连接 [{connectionId}] 因取消请求而关闭");
            }
            catch (Exception ex)
            {
                _logger.LogError($"连接 [{connectionId}] 处理失败：{ex.Message}", ex);
            }
            finally
            {
                stopwatch.Stop();

                // 使用ValueTask减少内存分配
                await ReleaseResourcesAsync(
                    client, clientStream,
                    targetClient, targetStream,
                    target, connectionId,
                    stopwatch.Elapsed);
            }
        }

        private async Task<Stream> ProcessClientSslAsync(TcpClient client, EndpointConfig ep, CancellationToken ct)
        {
            if (ep.Protocol != ConnectType.SslTcp)
                return client.GetStream();

            try
            {
                var certificate = GetOrLoadCertificate(ep.ServerCertificatePath);
                var sslStream = new SslStream(client.GetStream(), false, ValidateClientCertificate);

                await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = ep.ClientCertificateRequired,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    EncryptionPolicy = EncryptionPolicy.RequireEncryption
                }, ct);

                return sslStream;
            }
            catch (Exception ex)
            {
                _logger.LogError($"建立客户端SSL连接失败: {ex.Message}", ex);
                throw;
            }
        }

        private X509Certificate2 GetOrLoadCertificate(string certificatePath)
        {
            return _certificateCache.GetOrAdd(certificatePath, path =>
            {
                try
                {
                    return new X509Certificate2(path);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"加载证书失败: {ex.Message}", ex);
                    throw;
                }
            });
        }

        private async Task<TcpClient> GetOrCreateTargetConnectionAsync(TargetServer target, CancellationToken ct)
        {
            var poolKey = $"{target.Ip}:{target.TargetPort}";
            var pool = _connectionPools.GetOrAdd(poolKey, _ =>
                new ObjectPool<TcpClient>(() => new TcpClient(), MaxPooledConnections));

            using var timeoutCts = new CancellationTokenSource(ConnectionTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            TcpClient targetClient = null;
            bool isNewConnection = false;

            try
            {
                // 尝试从池中获取连接
                if (pool.TryGet(out targetClient))
                {
                    _logger.LogTrace($"从连接池获取连接：{poolKey}");

                    // 验证连接是否仍然有效
                    if (!IsConnectionAlive(targetClient))
                    {
                        targetClient.Dispose();
                        targetClient = null;
                    }
                }

                // 如果池为空或连接无效，则创建新连接
                if (targetClient == null)
                {
                    isNewConnection = true;
                    targetClient = new TcpClient();
                    await targetClient.ConnectAsync(target.Ip, target.TargetPort, linkedCts.Token);
                    _logger.LogTrace($"创建新连接：{poolKey}");
                }

                return targetClient;
            }
            catch (Exception ex)
            {
                // 确保异常发生时释放资源
                targetClient?.Dispose();

                if (isNewConnection)
                    _logger.LogError($"连接到目标服务器失败: {target.Ip}:{target.TargetPort}, 错误: {ex.Message}", ex);
                else
                    _logger.LogError($"从池获取的连接无效，创建新连接失败: {target.Ip}:{target.TargetPort}, 错误: {ex.Message}", ex);

                throw;
            }
        }

        private bool IsConnectionAlive(TcpClient client)
        {
            try
            {
                if (!client.Connected)
                    return false;

                var socket = client.Client;
                return !(socket.Poll(1000, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch
            {
                return false;
            }
        }

        private async Task<Stream> ProcessTargetSslAsync(TcpClient targetClient, TargetServer target, CancellationToken ct)
        {
            if (target.BackendProtocol != ConnectType.SslTcp)
                return targetClient.GetStream();

            try
            {
                var sslStream = new SslStream(targetClient.GetStream(), false, ValidateServerCertificate);

                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = target.Ip,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    EncryptionPolicy = EncryptionPolicy.RequireEncryption
                }, ct);

                return sslStream;
            }
            catch (Exception ex)
            {
                _logger.LogError($"建立目标服务器SSL连接失败: {target.Ip}:{target.TargetPort}, 错误: {ex.Message}", ex);
                throw;
            }
        }

        private bool ValidateServerCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None)
                return true;

            _logger.LogWarning($"目标服务器证书验证失败: {errors}");

            // 在开发环境中允许无效证书
            return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        }

        private async Task ForwardStreamsBidirectionalAsync(Stream source, Stream destination, string connectionId, CancellationToken ct)
        {
            try
            {
                // 使用管道进行高效双向数据传输
                var forwardTask = source.CopyToAsync(destination, BufferSize, ct);
                var reverseTask = destination.CopyToAsync(source, BufferSize, ct);

                await Task.WhenAny(forwardTask, reverseTask);

                // 当其中一个方向完成时，取消另一个方向
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug($"连接 [{connectionId}] 转发任务被取消");
            }
            catch (Exception ex)
            {
                _logger.LogError($"连接 [{connectionId}] 转发失败: {ex.Message}", ex);
                throw;
            }
        }

        private async ValueTask ReleaseResourcesAsync(
            TcpClient client, Stream clientStream,
            TcpClient targetClient, Stream targetStream,
            TargetServer target, string connectionId,
            TimeSpan duration)
        {
            try
            {
                // 关闭流
                if (clientStream != null)
                {
                    try { await clientStream.DisposeAsync(); }
                    catch (Exception ex) { _logger.LogWarning($"关闭客户端流失败: {ex.Message}"); }
                }

                if (targetStream != null)
                {
                    try { await targetStream.DisposeAsync(); }
                    catch (Exception ex) { _logger.LogWarning($"关闭目标流失败: {ex.Message}"); }
                }

                // 处理目标连接池
                if (targetClient != null && target != null)
                {
                    var poolKey = $"{target.Ip}:{target.TargetPort}";

                    if (_connectionPools.TryGetValue(poolKey, out var pool) &&
                        IsConnectionAlive(targetClient) &&
                        pool.Count < MaxPooledConnections)
                    {
                        pool.Return(targetClient);
                        _logger.LogTrace($"连接 [{connectionId}] 回收至连接池：{poolKey}");
                    }
                    else
                    {
                        try { targetClient.Dispose(); }
                        catch (Exception ex) { _logger.LogWarning($"释放目标客户端失败: {ex.Message}"); }
                    }
                }

                // 关闭客户端连接
                try { client?.Dispose(); }
                catch (Exception ex) { _logger.LogWarning($"释放客户端失败: {ex.Message}"); }

                // 更新指标
                if (target != null)
                {
                    UpdateMetrics(target, -1);
                    _logger.LogInformation($"连接 [{connectionId}] 关闭：{duration.TotalMilliseconds}ms，目标：{target.Ip}:{target.TargetPort}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"释放连接资源失败: {ex.Message}", ex);
            }
        }

        private bool ValidateClientCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None)
                return true;

            _logger.LogWarning($"客户端证书验证失败: {errors}");

            // 在开发环境中允许无效证书
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
                (_, m) =>
                {
                    Interlocked.Add(ref m.ActiveConnections, delta);
                    if (delta > 0)
                        Interlocked.Increment(ref m.TotalConnections);
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
                _connectionMetrics.Clear();
                _tcpMapServer.Clear();
                _certificateCache.Clear();
            }
        }
    }

    // 简单的对象池实现，减少GC压力
    public sealed class ObjectPool<T> : IDisposable where T : class, IDisposable
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;
        private readonly int _maxSize;
        private int _currentCount;
        private bool _disposed;

        public int Count => _objects.Count;

        public ObjectPool(Func<T> objectGenerator, int maxSize)
        {
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _maxSize = maxSize;
            _objects = new ConcurrentBag<T>();
        }

        public bool TryGet(out T item)
        {
            if (_objects.TryTake(out item))
                return true;

            if (Interlocked.Increment(ref _currentCount) <= _maxSize)
            {
                item = _objectGenerator();
                return true;
            }

            Interlocked.Decrement(ref _currentCount);
            item = null;
            return false;
        }

        public void Return(T item)
        {
            if (_disposed || _objects.Count >= _maxSize)
            {
                item.Dispose();
                return;
            }

            _objects.Add(item);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            while (_objects.TryTake(out var item))
            {
                item.Dispose();
            }
        }
    }

    // 轻量级Stopwatch实现，减少内存分配
    public readonly struct ValueStopwatch
    {
        private readonly long _startTimestamp;
        private readonly double _timestampToTicks;

        private ValueStopwatch(long startTimestamp, double timestampToTicks)
        {
            _startTimestamp = startTimestamp;
            _timestampToTicks = timestampToTicks;
        }

        public static ValueStopwatch StartNew() =>
            new ValueStopwatch(Stopwatch.GetTimestamp(), 10000000.0 / Stopwatch.Frequency);

        public TimeSpan Elapsed => TimeSpan.FromTicks((long)((Stopwatch.GetTimestamp() - _startTimestamp) * _timestampToTicks));

        public void Stop() { /* 此实现中不需要操作 */ }
    }
}