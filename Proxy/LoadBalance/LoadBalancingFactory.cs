using Server.Proxy.LoadBalance.Algorithm;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance
{
    public enum LoadBalancingAlgorithm
    {
        LeastConnections,    // 最小连接数
        RoundRobin,          // 轮询
        Random,              // 随机
        WeightedRoundRobin,  // 加权轮询
        Hash,                // 哈希
        LeastResponseTime,   // 最小响应时间
        ZoneAffinity         // 区域亲和性
    }

    public class LoadBalancingStrategyFactory
    {
        public static ILoadBalancingStrategy CreateStrategy(
            LoadBalancingAlgorithm algorithm,
            // 新增：哈希策略专用委托（其他策略使用 object 保持兼容）
            Func<HttpRequestMessage, string> hashKeySelector = null,
            string clientZone = null // 区域亲和性策略参数单独声明
        )
        {
            switch (algorithm)
            {
                case LoadBalancingAlgorithm.Hash:
                    if (hashKeySelector == null)
                    {
                        throw new ArgumentNullException(nameof(hashKeySelector), "哈希策略必须提供哈希键选择器");
                    }
                    return new HashStrategy(hashKeySelector); // 直接传递强类型委托

                case LoadBalancingAlgorithm.ZoneAffinity:
                    if (string.IsNullOrEmpty(clientZone))
                    {
                        throw new ArgumentNullException(nameof(clientZone), "区域亲和性策略必须提供客户端区域");
                    }
                    return new ZoneAffinityStrategy(clientZone);

                // 其他策略使用默认参数（无需上下文或使用 object）
                case LoadBalancingAlgorithm.LeastConnections:
                case LoadBalancingAlgorithm.RoundRobin:
                case LoadBalancingAlgorithm.Random:
                case LoadBalancingAlgorithm.WeightedRoundRobin:
                case LoadBalancingAlgorithm.LeastResponseTime:
                    return CreateDefaultStrategy(algorithm);

                default:
                    throw new ArgumentException($"不支持的算法: {algorithm}", nameof(algorithm));
            }
        }

        // 私有方法处理无上下文的策略
        private static ILoadBalancingStrategy CreateDefaultStrategy(LoadBalancingAlgorithm algorithm)
        {
            return algorithm switch
            {
                LoadBalancingAlgorithm.LeastConnections => new LeastConnectionsStrategy(),
                LoadBalancingAlgorithm.RoundRobin => new RoundRobinStrategy(),
                LoadBalancingAlgorithm.Random => new RandomStrategy(),
                LoadBalancingAlgorithm.WeightedRoundRobin => new WeightedRoundRobinStrategy(),
                LoadBalancingAlgorithm.LeastResponseTime => new LeastResponseTimeStrategy(),
                _ => throw new ArgumentException($"未知算法: {algorithm}"),
            };
        }
    }
}
