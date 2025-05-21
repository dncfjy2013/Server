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
{/// <summary>
 /// 负载均衡器接口，提供服务器选择和健康检查功能
 /// </summary>
    public interface ILoadBalancer
    {
        /// <summary>
        /// 根据配置选择目标服务器
        /// </summary>
        Task<TargetServer> SelectServerAsync(EndpointConfig config, object context = null);

        /// <summary>
        /// 检查指定端点的所有服务器健康状态
        /// </summary>
        Task CheckServerHealthAsync(EndpointConfig config);
    }
    /// <summary>
    /// 高级负载均衡器实现，支持多种负载均衡算法和服务器健康检查
    /// </summary>
    public class LoadBalancerSelect : ILoadBalancer
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<LoadBalancingAlgorithm, ILoadBalancingStrategy> _strategyCache = new();

        public LoadBalancerSelect(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<TargetServer> SelectServerAsync(EndpointConfig config, object context = null)
        {
            var healthyServers = config.TargetServers
                .Where(s => s.IsHealthy)
                .ToList();

            if (!healthyServers.Any())
            {
                _logger.LogWarning("无健康服务器，尝试重新检查所有服务器状态");
                await CheckServerHealthAsync(config);
                healthyServers = config.TargetServers.Where(s => s.IsHealthy).ToList();

                if (!healthyServers.Any())
                    throw new InvalidOperationException("所有目标服务器均不健康");
            }

            var strategy = _strategyCache.GetOrAdd(
                config.LoadBalancingAlgorithm,
                algo => CreateStrategy(algo, context)
            );

            return strategy.SelectServer(healthyServers, context);
        }

        public async Task CheckServerHealthAsync(EndpointConfig config)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            foreach (var server in config.TargetServers)
            {
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(
                        IPAddress.Parse(server.Ip),
                        server.TargetPort,
                        cts.Token
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

        private ILoadBalancingStrategy CreateStrategy(LoadBalancingAlgorithm algo, object context)
        {
            return algo switch
            {
                LoadBalancingAlgorithm.Hash =>
                    CreateHashStrategy(context),

                LoadBalancingAlgorithm.ZoneAffinity =>
                    CreateZoneAffinityStrategy(context),

                _ => LoadBalancingStrategyFactory.CreateStrategy(algo)
            };
        }

        private ILoadBalancingStrategy CreateZoneAffinityStrategy(object context)
        {
            string clientZone = GetClientZone(context);
            return LoadBalancingStrategyFactory.CreateStrategy(
                LoadBalancingAlgorithm.ZoneAffinity,
                clientZone: clientZone
            );
        }

        private ILoadBalancingStrategy CreateHashStrategy(object context)
        {
            if (context is HttpListenerContext httpContext)
            {
                string hashKey = ExtractHashKeyFromHttpRequest(httpContext);
                return LoadBalancingStrategyFactory.CreateStrategy(
                    LoadBalancingAlgorithm.Hash,
                    _ => hashKey
                );
            }
            else if (context is Func<HttpRequestMessage, string> hashKeySelector)
            {
                return LoadBalancingStrategyFactory.CreateStrategy(
                    LoadBalancingAlgorithm.Hash,
                    hashKeySelector
                );
            }
            else if (context is string staticHashKey)
            {
                return LoadBalancingStrategyFactory.CreateStrategy(
                    LoadBalancingAlgorithm.Hash,
                    _ => staticHashKey
                );
            }

            throw new ArgumentException("哈希策略需要有效的上下文参数", nameof(context));
        }

        private string ExtractHashKeyFromHttpRequest(HttpListenerContext context)
        {
            string[] candidateHeaders = { "X-Request-ID", "X-Session-ID", "X-User-ID" };
            foreach (string header in candidateHeaders)
            {
                string value = context.Request.Headers[header];
                if (!string.IsNullOrEmpty(value))
                    return value;
            }

            var sessionCookie = context.Request.Cookies["SESSION_ID"];
            if (sessionCookie != null && !string.IsNullOrEmpty(sessionCookie.Value))
            {
                return sessionCookie.Value;
            }

            string querySessionId = context.Request.QueryString["session_id"];
            if (!string.IsNullOrEmpty(querySessionId))
            {
                return querySessionId;
            }

            return context.Request.RemoteEndPoint.Address.ToString();
        }

        private string GetClientZone(object context)
        {
            // 实现客户端区域提取逻辑
            if (context is HttpListenerContext httpContext)
            {
                // 从HTTP上下文提取区域信息
                return httpContext.Request.Headers["X-Client-Zone"] ?? "default";
            }

            // 其他上下文类型的处理...
            return "default";
        }
    }

}
