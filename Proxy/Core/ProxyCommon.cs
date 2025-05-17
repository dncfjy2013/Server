using Server.Proxy.Config;
using Server.Proxy.LoadBalance;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    sealed partial class AdvancedPortForwarder
    {
        #region 通用辅助方法
        private readonly ConcurrentDictionary<LoadBalancingAlgorithm, ILoadBalancingStrategy> _strategyCache = new();
        #region 区域亲和性和响应时间统计

        #endregion
        private async Task CheckServerHealthAsync(EndpointConfig ep)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // 创建带超时的 CancellationToken

            foreach (var server in ep.TargetServers)
            {
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(
                        IPAddress.Parse(server.Ip), // 解析 IP 字符串
                        server.TargetPort,
                        cts.Token // 传递 CancellationToken 实现超时
                    );
                    server.IsHealthy = true;
                }
                catch (OperationCanceledException)
                {
                    server.IsHealthy = false;
                    _logger.LogWarning($"服务器 {server.Ip}:{server.TargetPort} 连接超时");
                }
                catch (Exception ex)
                {
                    server.IsHealthy = false;
                    _logger.LogWarning($"服务器 {server.Ip}:{server.TargetPort} 健康检查失败: {ex.Message}");
                }
            }
        }
        private void UpdateResponseTime(TargetServer target, long elapsedMs)
        {
            // 使用线程安全的方式更新平均响应时间（例如滑动窗口或指数加权平均）
            target.AverageResponseTimeMs =
                (target.AverageResponseTimeMs * target.RequestCount + elapsedMs) /
                (target.RequestCount + 1);

            target.RequestCount++; // 记录请求次数
        }
        /// <summary>
        /// 负载均衡算法
        /// </summary>
        private async Task<TargetServer> SelectServerAsync(EndpointConfig ep, object context = null)
        {
            var healthyServers = ep.TargetServers
                .Where(s => s.IsHealthy)
                .ToList();

            if (!healthyServers.Any())
            {
                // 触发告警或回退逻辑（如使用缓存健康服务器或报错）
                _logger.LogWarning("无健康服务器，尝试重新检查所有服务器状态");
                await CheckServerHealthAsync(ep); // 重新检查健康状态
                healthyServers = ep.TargetServers
                    .Where(s => s.IsHealthy)
                    .ToList();
                if (!healthyServers.Any())
                {
                    throw new InvalidOperationException("所有目标服务器均不健康");
                }
            }

            // 从缓存获取或创建策略实例
            var strategy = _strategyCache.GetOrAdd(
                ep.LoadBalancingAlgorithm,
                algo =>
                {
                    // 根据算法类型传递不同的上下文参数
                    switch (algo)
                    {
                        case LoadBalancingAlgorithm.Hash:
                            // 从 context 中提取哈希键选择器
                            if (context is Func<HttpRequestMessage, string> hashKeySelector)
                            {
                                return LoadBalancingStrategyFactory.CreateStrategy(algo, hashKeySelector);
                            }
                            throw new ArgumentException("哈希策略需要提供哈希键选择器");

                        case LoadBalancingAlgorithm.ZoneAffinity:

                            // 获取客户端区域（优化：提前提取并缓存）
                            string clientZone = GetClientZone((HttpListenerContext)context);

                            return LoadBalancingStrategyFactory.CreateStrategy(algo, clientZone: clientZone);

                        default:
                            // 其他策略不需要上下文参数
                            return LoadBalancingStrategyFactory.CreateStrategy(algo);
                    }
                }
            );

            // 调用策略时传递请求上下文
            return strategy.SelectServer(healthyServers, context);
        }
        /// <summary>
        /// 双向数据流转发（支持取消和错误处理）
        /// 优化点：
        /// • 使用ArrayPool共享缓冲区（避免频繁内存分配）
        /// • 异步流式读写（支持大文件传输）
        /// • 区分正向/反向日志（便于问题定位）
        /// </summary>
        private async Task ForwardStreamsAsync(Stream source, Stream destination, string connectionId,
            CancellationToken ct, bool isReverse = false)
        {
            try
            {
                // 从数组池获取缓冲区（默认8KB大小，可根据场景调整）
                var buffer = ArrayPool<byte>.Shared.Rent(8192);
                try
                {
                    int bytesRead;
                    // 循环读取直到流结束或取消
                    while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await destination.WriteAsync(buffer, 0, bytesRead, ct);
                        await destination.FlushAsync(ct); // 强制刷新（某些流需要）
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer); // 归还缓冲区到数组池
                }
            }

            catch (OperationCanceledException)
            {
                _logger.LogDebug($"数据流转发 [{connectionId}] {(isReverse ? "反向 目标→客户端" : "正向 客户端→目标")} 已取消");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"数据流转发 [{connectionId}] {(isReverse ? "反向 目标→客户端" : "正向 客户端→目标")} 已取消");
            }
        }

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
        #endregion

    }
}
