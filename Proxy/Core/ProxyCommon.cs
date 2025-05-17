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
                _logger.LogWarning("无健康服务器，尝试重新检查所有服务器状态");
                await CheckServerHealthAsync(ep);
                healthyServers = ep.TargetServers.Where(s => s.IsHealthy).ToList();
                if (!healthyServers.Any())
                    throw new InvalidOperationException("所有目标服务器均不健康");
            }

            // 从缓存获取或创建策略实例（优化哈希键提取）
            var strategy = _strategyCache.GetOrAdd(
                ep.LoadBalancingAlgorithm,
                algo => CreateStrategy(algo, context)
            );

            return strategy.SelectServer(healthyServers, context);
        }

        private ILoadBalancingStrategy CreateStrategy(LoadBalancingAlgorithm algo, object context)
        {
            return algo switch
            {
                LoadBalancingAlgorithm.Hash =>
                    CreateHashStrategy(context),  // 独立的哈希策略创建方法

                LoadBalancingAlgorithm.ZoneAffinity =>
                    CreateZoneAffinityStrategy(context),  // 保持原有区域逻辑

                _ => LoadBalancingStrategyFactory.CreateStrategy(algo)
            };
        }
        private ILoadBalancingStrategy CreateZoneAffinityStrategy(object context)
        {
            // 获取客户端区域（优化：提前提取并缓存）
            string clientZone = GetClientZone((HttpListenerContext)context);

            return LoadBalancingStrategyFactory.CreateStrategy(LoadBalancingAlgorithm.ZoneAffinity, clientZone: clientZone);
        }
        private ILoadBalancingStrategy CreateHashStrategy(object context)
        {
            // 安全转换上下文类型（支持HttpListenerContext和直接传递哈希键）
            if (context is HttpListenerContext httpContext)
            {
                // 从HTTP请求中提取哈希键（支持多来源）
                string hashKey = ExtractHashKeyFromHttpRequest(httpContext);
                return LoadBalancingStrategyFactory.CreateStrategy(
                    LoadBalancingAlgorithm.Hash,
                    _ => hashKey  // 固定哈希键选择器
                );
            }
            else if (context is Func<HttpRequestMessage, string> hashKeySelector)
            {
                // 保留原有自定义哈希键逻辑
                return LoadBalancingStrategyFactory.CreateStrategy(
                    LoadBalancingAlgorithm.Hash,
                    hashKeySelector
                );
            }
            else if (context is string staticHashKey)
            {
                // 支持静态哈希键（如测试场景）
                return LoadBalancingStrategyFactory.CreateStrategy(
                    LoadBalancingAlgorithm.Hash,
                    _ => staticHashKey
                );
            }

            // 回退：使用客户端IP作为哈希键（与原有逻辑一致）
            throw new ArgumentException("哈希策略需要有效的上下文参数", nameof(context));
        }

        private string ExtractHashKeyFromHttpRequest(HttpListenerContext context)
        {
            // 1. 从自定义请求头获取（优先级最高）
            string[] candidateHeaders = { "X-Request-ID", "X-Session-ID", "X-User-ID" };
            foreach (string header in candidateHeaders)
            {
                string value = context.Request.Headers[header];
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            // 2. 从Cookie中提取会话ID（.NET 6 兼容写法）
            Cookie sessionCookie = context.Request.Cookies["SESSION_ID"];
            if (sessionCookie != null && !string.IsNullOrEmpty(sessionCookie.Value))
            {
                return sessionCookie.Value;
            }

            // 3. 从URL参数中提取（如果适用）
            string querySessionId = context.Request.QueryString["session_id"];
            if (!string.IsNullOrEmpty(querySessionId))
            {
                return querySessionId;
            }

            // 4. 回退到客户端IP
            return context.Request.RemoteEndPoint.Address.ToString();
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

        #endregion
    }
}
