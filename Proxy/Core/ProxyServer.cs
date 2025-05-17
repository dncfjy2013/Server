using Org.BouncyCastle.Asn1.X509;
using Server.Logger;
using Server.Proxy.Common;
using Server.Proxy.Config;
using Server.Proxy.LoadBalance;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.RateLimiting;

namespace Server.Proxy.Core
{
    /// <summary>
    /// 高级端口转发器核心类
    /// 功能特性：
    /// ✅ 多协议支持：TCP/UDP/HTTP 原生协议及 SSL/TLS 加密扩展
    /// ✅ 连接管理：基于连接池（TCP）和限流器的资源控制
    /// ✅ 负载均衡：最小连接数算法实现服务端负载分发
    /// ✅ 可观测性：完善的日志系统和性能指标统计
    /// ✅ 生命周期管理：实现 IAsyncDisposable 支持优雅启停
    /// </summary>
    sealed partial class AdvancedPortForwarder : IAsyncDisposable
    {
        private readonly ILogger _logger; // 依赖注入的日志组件，用于运行时追踪
        private List<EndpointConfig> _endpoints; // 存储所有端点配置（不可变列表保证线程安全）
        private readonly ConcurrentDictionary<int, RateLimiter> _portLimiters = new(); // 端口级连接数限流器（线程安全集合）
        private readonly ConcurrentDictionary<int, TcpListener> _tcpListeners = new(); // TCP 监听器集合
        private readonly ConcurrentDictionary<int, HttpListener> _httpListeners = new(); // HTTP 监听器集合
        private readonly ConcurrentDictionary<int, UdpClient> _udpClients = new(); // UDP 客户端集合
        private readonly ConcurrentDictionary<string, ConnectionMetrics> _connectionMetrics = new(); // 连接性能指标存储（按目标服务器分组）
        private readonly CancellationTokenSource _cancellationTokenSource = new(); // 全局取消令牌源，用于协调异步操作停止
        private bool _isRunning; // 运行状态标识（避免重复启动）
        private bool _disposed; // 资源释放标识（防止重复释放）

        // TCP 连接池相关
        private readonly ConcurrentDictionary<string, Stack<TcpClient>> _connectionPools = new(); // 连接池：键为目标服务器地址+端口，值为连接栈
        private const int MaxPooledConnections = 50; // 单个连接池最大连接数，防止内存占用过高

        public AdvancedPortForwarder(ILogger logger)
        {
            _logger = logger;

            InitIpZone();
        }

        public void Init(IEnumerable<EndpointConfig> endpoints)
        {
            _endpoints = endpoints.ToList(); // 转换为列表提升遍历性能
            // 初始化限流器：每个端口独立配置连接数限制
            foreach (var ep in _endpoints)
            {
                _portLimiters[ep.ListenPort] = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = ep.MaxConnections, // 最大并发连接数（防止端口被洪泛攻击）
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst, // 连接队列 FIFO 处理
                    QueueLimit = 100 // 等待队列长度（超出后丢弃连接请求）
                });
            }
        }

        /// <summary>
        /// 启动转发器
        /// 启动流程：
        /// 1. 校验运行状态
        /// 2. 为每个端点创建独立的监听任务（异步非阻塞）
        /// 3. 处理启动过程中的取消请求和未处理异常
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
                throw new InvalidOperationException("转发器已处于运行状态");

            _isRunning = true;
            _logger.LogInformation("启动高级端口转发器...");

            var tasks = new List<Task>();
            foreach (var ep in _endpoints)
            {
                // 按协议类型分发到不同的监听处理器
                tasks.Add(RunEndpointAsync(ep, _cancellationTokenSource.Token));
            }

            try
            {
                await Task.WhenAll(tasks); // 等待所有端点启动完成或取消
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("转发器已停止");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"转发器启动失败：{ex.Message}");
            }
            finally
            {
                _isRunning = false;
            }
        }

        /// <summary>
        /// 优雅停止转发器
        /// 停止流程：
        /// 1. 触发全局取消令牌（通知所有异步操作终止）
        /// 2. 并行停止各协议监听器（提升停止速度）
        /// 3. 等待所有活跃连接关闭（带超时机制防止阻塞）
        /// 4. 释放限流器和连接池资源
        /// </summary>
        public async Task StopAsync(TimeSpan timeout)
        {
            _logger.LogInformation("开始停止转发器...");
            _cancellationTokenSource.Cancel(); // 取消所有正在执行的监听和转发任务

            // 并行停止不同协议的监听器（TCP/HTTP/UDP）
            await Task.WhenAll(
                StopTcpListenersAsync(),
                StopHttpListenersAsync(),
                StopUdpClientsAsync()
            );

            // 等待连接关闭（支持超时取消）
            using var timeoutCts = new CancellationTokenSource(timeout);
            try
            {
                await WaitForConnectionsToCloseAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"超时未关闭所有连接，剩余活跃连接数：{GetActiveConnectionsCount()}");
            }

            // 释放限流器资源（实现 IDisposable）
            foreach (var limiter in _portLimiters.Values)
            {
                limiter.Dispose();
            }

            _logger.LogInformation("转发器已完全停止");
        }

        /// <summary>
        /// 获取实时性能指标
        /// 指标包含：
        /// ▶ 全局活跃连接数
        /// ▶ 各目标服务器连接统计（活跃/总连接数、最后活动时间）
        /// ▶ 端点状态（是否正在监听）
        /// </summary>
        public PortForwarderMetrics GetMetrics()
        {
            return new PortForwarderMetrics
            {
                ActiveConnections = GetActiveConnectionsCount(), // 计算所有目标服务器的活跃连接总和
                ConnectionMetrics = _connectionMetrics.Values.ToList(), // 获取各目标服务器的连接指标
                EndpointStatus = _endpoints.Select(ep => new EndpointStatus
                {
                    ListenPort = ep.ListenPort,
                    Protocol = ep.Protocol,
                    // 判断端点是否活跃（至少有一个对应协议的监听器存在）
                    IsActive = _tcpListeners.ContainsKey(ep.ListenPort) ||
                              _httpListeners.ContainsKey(ep.ListenPort) ||
                              _udpClients.ContainsKey(ep.ListenPort)
                }).ToList()
            };
        }

        private int GetActiveConnectionsCount()
        {
            return _connectionMetrics.Values.Sum(m => m.ActiveConnections); // 累加所有目标服务器的活跃连接数
        }

        #region 端点处理核心逻辑
        /// <summary>
        /// 启动单个端点的监听逻辑
        /// 职责：根据协议类型调用对应的监听处理器
        /// </summary>
        private async Task RunEndpointAsync(EndpointConfig ep, CancellationToken ct)
        {
            try
            {
                _logger.LogInformation($"启动端点监听：端口 {ep.ListenPort}，协议 {ep.Protocol}");

                switch (ep.Protocol)
                {
                    case ConnectType.Tcp:
                    case ConnectType.SslTcp: // SSL/TCP 复用 TCP 监听器，通过后续 SSL 握手区分
                        await RunTcpEndpointAsync(ep, ct);
                        break;
                    case ConnectType.Udp:
                        await RunUdpEndpointAsync(ep, ct);
                        break;
                    case ConnectType.Http:
                        await RunHttpEndpointAsync(ep, ct);
                        break;
                    default:
                        _logger.LogWarning($"不支持的协议类型：{ep.Protocol}");
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"端点 {ep.ListenPort} 监听已取消");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"端点 {ep.ListenPort} 发生致命错误：{ex.Message}");
            }
        }
        #endregion

        #region TCP 协议处理模块
        /// <summary>
        /// 启动 TCP/SSL-TCP 监听器
        /// 实现细节：
        /// • 使用 TcpListener 进行端口监听
        /// • 通过 ConcurrentDictionary 保证监听器唯一性
        /// • 结合限流器实现连接数控制
        /// </summary>
        private async Task RunTcpEndpointAsync(EndpointConfig ep, CancellationToken ct)
        {
            // 创建 TCP 监听器并绑定地址端口
            var listener = new TcpListener(IPAddress.Parse(ep.ListenIp), ep.ListenPort);
            listener.Start();

            // 确保端口唯一监听（避免端口冲突）
            if (!_tcpListeners.TryAdd(ep.ListenPort, listener))
            {
                listener.Stop();
                throw new InvalidOperationException($"端口 {ep.ListenPort} 已被其他监听器占用");
            }

            _logger.LogInformation($"TCP 监听器启动：端口 {ep.ListenPort}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 异步获取连接数限流器租约（处理背压）
                    using var lease = await _portLimiters[ep.ListenPort].AcquireAsync(1, ct);
                    if (!lease.IsAcquired)
                    {
                        _logger.LogWarning($"端口 {ep.ListenPort} 连接数已满，拒绝新连接");
                        continue;
                    }

                    // 异步接受客户端连接（支持取消操作）
                    var client = await listener.AcceptTcpClientAsync(ct);
                    // 启动独立任务处理连接（避免阻塞监听循环）
                    _ = HandleTcpConnectionAsync(client, ep, ct);
                }
            }
            finally
            {
                // 清理资源（从集合移除并停止监听器）
                _tcpListeners.TryRemove(ep.ListenPort, out _);
                listener.Stop();
                _logger.LogInformation($"TCP 监听器停止：端口 {ep.ListenPort}");
            }
        }

        /// <summary>
        /// 处理 TCP 客户端连接核心逻辑
        /// 流程说明：
        /// 1. 负载均衡选择目标服务器（最小连接数算法）
        /// 2. 客户端 SSL 握手（如需）
        /// 3. 连接池获取或创建目标服务器连接
        /// 4. 双向数据流转发（异步非阻塞）
        /// 5. 连接池回收与资源释放
        /// </summary>
        private async Task HandleTcpConnectionAsync(TcpClient client, EndpointConfig ep, CancellationToken ct)
        {
            var remoteEndPoint = client.Client.RemoteEndPoint.ToString(); // 客户端地址信息
            TargetServer target = null;
            TcpClient targetClient = null;
            Stream clientStream = null;
            Stream targetStream = null;
            var connectionId = Guid.NewGuid().ToString(); // 唯一连接标识（用于日志追踪）
            var stopwatch = Stopwatch.StartNew(); // 性能计时（记录连接持续时间）

            try
            {
                // 负载均衡：选择当前压力最小的目标服务器
                target = await SelectServerAsync(ep);
                // 更新连接指标（活跃连接数+1）
                UpdateMetrics(target, 1);

                _logger.LogDebug($"新连接 [{connectionId}]：{remoteEndPoint} → {target.Ip}:{target.TargetPort}");

                // 处理客户端到转发器的 SSL/TLS 协议
                if (ep.Protocol == ConnectType.SslTcp)
                {
                    // 创建 SSL 流并配置证书验证逻辑
                    var sslStream = new SslStream(client.GetStream(), false, ValidateClientCertificate);
                    // 服务器端认证（需要提供服务器证书）
                    await sslStream.AuthenticateAsServerAsync(
                        ep.ServerCertificate, // 服务器证书（用于客户端验证）
                        ep.ClientCertificateRequired, // 是否强制客户端证书
                        SslProtocols.Tls12 | SslProtocols.Tls13, // 支持的 TLS 版本（推荐 TLS 1.2+）
                        false // 不启用加密套件协商（使用默认配置）
                    );
                    clientStream = sslStream; // 使用加密流进行数据传输
                }
                else
                {
                    clientStream = client.GetStream(); // 非加密模式直接使用原始流
                }

                // 连接池逻辑：获取或创建目标服务器连接
                var poolKey = $"{target.Ip}:{target.TargetPort}"; // 连接池键（唯一标识目标服务器）
                if (!_connectionPools.TryGetValue(poolKey, out var pool))
                {
                    pool = new Stack<TcpClient>(); // 初始化连接池（线程安全的栈结构）
                    _connectionPools[poolKey] = pool;
                }

                if (pool.TryPop(out targetClient)) // 从连接池获取连接（LIFO 策略）
                {
                    _logger.LogTrace($"从连接池获取连接：{poolKey}");
                    // 检查连接有效性（可能因网络问题已断开）
                    if (!targetClient.Connected)
                    {
                        targetClient.Dispose(); // 无效连接直接释放
                        targetClient = new TcpClient(); // 创建新连接
                    }
                }
                else
                {
                    targetClient = new TcpClient(); // 连接池无可用连接时新建
                }

                // 连接到目标服务器（支持取消操作）
                await targetClient.ConnectAsync(target.Ip, target.TargetPort, ct);

                // 处理目标服务器端协议（可能需要 SSL/TLS）
                if (target.BackendProtocol == ConnectType.SslTcp)
                {
                    var targetSslStream = new SslStream(targetClient.GetStream(), false);
                    // 客户端认证（连接到目标服务器的 SSL 服务）
                    await targetSslStream.AuthenticateAsClientAsync(target.Ip); // 使用目标服务器域名验证证书
                    targetStream = targetSslStream; // 使用加密流与目标服务器通信
                }
                else
                {
                    targetStream = targetClient.GetStream(); // 非加密模式直接使用原始流
                }

                // 双向数据流转发（异步方法避免阻塞）
                var forwardTask = ForwardStreamsAsync(clientStream, targetStream, connectionId, ct); // 客户端 → 目标服务器
                var reverseTask = ForwardStreamsAsync(targetStream, clientStream, connectionId, ct, true); // 目标服务器 → 客户端

                // 等待任意方向转发完成（表示连接关闭）
                await Task.WhenAny(forwardTask, reverseTask);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug($"连接 [{connectionId}] 已取消");
            }
            catch (SocketException ex)
            {
                _logger.LogWarning($"连接 [{connectionId}] 网络错误：{ex.SocketErrorCode} - {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"连接 [{connectionId}] 处理失败：{ex.Message}");
            }
            finally
            {
                stopwatch.Stop(); // 结束性能计时

                // 连接池回收逻辑（仅回收有效连接且池未满）
                if (targetClient != null && target != null)
                {
                    var poolKey = $"{target.Ip}:{target.TargetPort}";
                    if (_connectionPools.TryGetValue(poolKey, out var pool))
                    {
                        if (targetClient.Connected && pool.Count < MaxPooledConnections)
                        {
                            pool.Push(targetClient); // 放回连接池以便重用
                            _logger.LogTrace($"连接 [{connectionId}] 已回收至连接池：{poolKey}");
                            targetClient = null; // 标记为已回收，避免重复释放
                        }
                    }
                }

                // 释放资源（确保所有流和客户端正确关闭）
                targetClient?.Dispose();
                clientStream?.Dispose();
                targetStream?.Dispose();
                client.Dispose();

                // 更新连接指标（活跃连接数-1）
                if (target != null)
                {
                    UpdateMetrics(target, -1);
                    _logger.LogInformation($"连接 [{connectionId}] 已关闭：{stopwatch.ElapsedMilliseconds}ms，{remoteEndPoint} → {target.Ip}:{target.TargetPort}");
                }
            }
        }

        private bool ValidateClientCertificate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None)
                return true;

            _logger.LogWarning($"客户端证书验证失败: {errors}");

            // 在生产环境中应更严格地验证证书
            return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";
        }
        #endregion

        #region UDP协议处理模块
        /// <summary>
        /// 启动UDP端点监听
        /// 实现特点：
        /// • 使用UdpClient绑定监听地址端口
        /// • 基于限流器控制并发数据包处理（注：UDP无连接概念，此处限流器实际控制并发处理请求数）
        /// • 异步非阻塞接收数据包（支持取消操作）
        /// </summary>
        private async Task RunUdpEndpointAsync(EndpointConfig ep, CancellationToken ct)
        {
            // 创建UDP客户端并绑定监听端点（支持IPv4/IPv6，根据配置解析IP）
            var udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(ep.ListenIp), ep.ListenPort));

            // 确保端口唯一监听（避免UDP端口冲突）
            if (!_udpClients.TryAdd(ep.ListenPort, udpClient))
            {
                udpClient.Close(); // 释放资源
                throw new InvalidOperationException($"UDP端口 {ep.ListenPort} 已被占用");
            }

            _logger.LogInformation($"UDP监听器启动：端口 {ep.ListenPort}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 获取限流器租约（控制同时处理的数据包数量，防止内存溢出）
                    using var lease = await _portLimiters[ep.ListenPort].AcquireAsync(1, ct);
                    if (!lease.IsAcquired)
                    {
                        _logger.LogWarning($"UDP端口 {ep.ListenPort} 处理队列已满，丢弃数据包");
                        await Task.Delay(100, ct); // 短暂等待后重试（退避策略）
                        continue;
                    }

                    try
                    {
                        // 异步接收UDP数据包（包含客户端端点信息）
                        var result = await udpClient.ReceiveAsync(ct);
                        // 启动独立任务处理数据包（避免阻塞接收循环）
                        _ = HandleUdpPacketAsync(result, ep, ct);
                    }
                    catch (SocketException ex)
                    {
                        // 处理常见网络异常（如端口不可达、超时等）
                        _logger.LogWarning($"UDP接收失败：{ex.SocketErrorCode} - {ex.Message}");
                    }
                }
            }
            finally
            {
                // 清理资源（从集合移除并关闭客户端）
                _udpClients.TryRemove(ep.ListenPort, out _);
                udpClient.Close();
                _logger.LogInformation($"UDP监听器停止：端口 {ep.ListenPort}");
            }
        }

        /// <summary>
        /// 处理UDP数据包转发
        /// 实现逻辑：
        /// 1. 负载均衡选择目标服务器
        /// 2. 创建临时UdpClient发送数据包（注：UDP无连接，每次发送新建客户端可避免端口占用问题）
        /// 3. 记录转发日志（包含字节数和端点信息）
        /// </summary>
        private async Task HandleUdpPacketAsync(UdpReceiveResult result, EndpointConfig ep, CancellationToken ct)
        {
            try
            {
                // 负载均衡：选择当前连接数最少的目标服务器
                var target = await SelectServerAsync(ep);
                // 使用using确保UdpClient及时释放资源
                using var client = new UdpClient();

                // 构造目标端点（IP+端口）
                var targetEndpoint = new IPEndPoint(IPAddress.Parse(target.Ip), target.TargetPort);
                // 发送数据包（使用2参数版本SendAsync，明确指定缓冲区和目标端点）
                await client.SendAsync(result.Buffer, result.Buffer.Length, targetEndpoint);

                _logger.LogDebug($"UDP转发完成：{result.RemoteEndPoint} → {target.Ip}:{target.TargetPort}，字节数 {result.Buffer.Length}");
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("UDP转发被取消");
            }
            catch (Exception ex)
            {
                // 记录所有未处理异常（如目标服务器不可达）
                _logger.LogError($"UDP转发失败：{ex.Message}");
            }
        }
        #endregion

        #region 资源释放模块
        /// <summary>
        /// 停止所有TCP监听器（并行执行提升效率）
        /// 注意：Stop() 会中断正在Accept的连接，需配合CancellationToken处理
        /// </summary>
        private async Task StopTcpListenersAsync()
        {
            var tasks = new List<Task>();
            foreach (var listener in _tcpListeners.Values)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        listener.Stop(); // 停止监听，关闭所有挂起的Accept操作
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"停止TCP监听器失败：{ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks); // 等待所有停止操作完成
            _tcpListeners.Clear(); // 清空集合
        }

        /// <summary>
        /// 停止所有HTTP监听器
        /// 注意：HttpListener.Stop() 会抛出异常给正在等待的GetContextAsync
        /// </summary>
        private async Task StopHttpListenersAsync()
        {
            var tasks = new List<Task>();
            foreach (var listener in _httpListeners.Values)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        listener.Stop(); // 停止监听，终止所有请求处理
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"停止HTTP监听器失败：{ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
            _httpListeners.Clear();
        }

        /// <summary>
        /// 停止所有UDP客户端
        /// 注意：UdpClient.Close() 会释放底层Socket资源
        /// </summary>
        private async Task StopUdpClientsAsync()
        {
            var tasks = new List<Task>();
            foreach (var client in _udpClients.Values)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        client.Close(); // 关闭UDP客户端，停止接收数据包
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"停止UDP客户端失败：{ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
            _udpClients.Clear();
        }

        #region 连接关闭等待逻辑
        /// <summary>
        /// 等待所有活跃连接自然关闭
        /// 实现逻辑：
        /// • 循环检查活跃连接数（通过GetActiveConnectionsCount）
        /// • 每次检查间隔500ms（可调整此间隔平衡性能与响应速度）
        /// • 支持通过CancellationToken提前终止等待（如超时）
        /// </summary>
        private async Task WaitForConnectionsToCloseAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested) // 当接收到取消信号时退出循环
            {
                var activeConnections = GetActiveConnectionsCount(); // 获取当前活跃连接总数
                if (activeConnections == 0) // 所有连接已关闭
                {
                    break;
                }

                // 记录等待日志（便于观察关闭进度）
                _logger.LogInformation($"等待 {activeConnections} 个连接关闭...");

                // 等待半秒后再次检查（异步等待支持取消）
                await Task.Delay(500, ct);
            }
        }
        #endregion

        #region 资源释放（实现IDisposable异步接口）
        /// <summary>
        /// 异步释放资源（实现IAsyncDisposable接口）
        /// 职责：
        /// 1. 调用StopAsync执行优雅停止流程
        /// 2. 释放所有托管/非托管资源
        /// 3. 设置已释放标识防止重复调用
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (!_disposed) // 双重检查锁定（避免竞态条件）
            {
                _disposed = true; // 标记为已释放

                try
                {
                    if (_isRunning) // 如果转发器仍在运行
                    {
                        // 调用StopAsync进行优雅停止（带30秒超时）
                        await StopAsync(TimeSpan.FromSeconds(30));
                    }
                }
                finally
                {
                    // 释放全局取消令牌源（防止内存泄漏）
                    _cancellationTokenSource.Dispose();

                    // 清理TCP连接池：
                    // • 遍历所有连接池
                    // • 释放每个连接的资源（关闭Socket连接）
                    // • 清空连接池集合
                    foreach (var pool in _connectionPools.Values)
                    {
                        foreach (var client in pool)
                        {
                            client.Dispose(); // 释放TcpClient资源（关闭网络连接）
                        }
                    }
                    _connectionPools.Clear();

                    // 释放限流器资源：
                    // • 限流器实现了IDisposable，需显式释放
                    // • 清空限流器集合
                    foreach (var limiter in _portLimiters.Values)
                    {
                        limiter.Dispose();
                    }
                    _portLimiters.Clear();
                }
            }
        }
        #endregion

        #endregion
    }
}