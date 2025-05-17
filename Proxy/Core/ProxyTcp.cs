using Server.Proxy.Common;
using Server.Proxy.Config;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    sealed partial class AdvancedPortForwarder
    {
        private readonly ConcurrentDictionary<int, TcpListener> _tcpListeners = new(); // TCP 监听器集合

        private readonly ConcurrentDictionary<string, Stack<TcpClient>> _connectionPools = new(); // 连接池：键为目标服务器地址+端口，值为连接栈
        private const int MaxPooledConnections = 50; // 单个连接池最大连接数，防止内存占用过高

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
        /// <summary>
        /// 更新连接性能指标
        /// 实现逻辑：
        /// • 使用ConcurrentDictionary保证线程安全
        /// • 原子操作更新活跃连接数（通过Interlocked）
        /// • 记录总连接数和最后活动时间
        /// </summary>
        private void UpdateMetrics(TargetServer target, int delta)
        {
            var key = $"{target.Ip}:{target.TargetPort}";

            // 原子更新目标服务器连接数（线程安全）
            if (delta > 0)
                target.Increment(); // Interlocked.Increment
            else
                target.Decrement(); // Interlocked.Decrement

            // 更新或添加连接指标（使用线程安全的AddOrUpdate）
            _connectionMetrics.AddOrUpdate(
                key,
                // 新增条目时初始化
                _ => new ConnectionMetrics
                {
                    Target = key,
                    ActiveConnections = delta,
                    TotalConnections = delta > 0 ? 1 : 0,
                    LastActivity = DateTime.UtcNow
                },
                // 现有条目时更新
                (_, metrics) =>
                {
                    metrics.ActiveConnections += delta;
                    if (delta > 0)
                        metrics.TotalConnections++; // 每个新增连接计数+1
                    metrics.LastActivity = DateTime.UtcNow; // 更新最后活动时间
                    return metrics;
                });
        }
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
    }
}
