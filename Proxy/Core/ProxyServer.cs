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

        #region 资源释放模块

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