using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance
{
    public enum LoadBalancingAlgorithm
    {
        LeastConnections,
        RoundRobin,
        Random,
        WeightedRoundRobin
    }

    public class LoadBalancingStrategyFactory
    {
        public static ILoadBalancingStrategy CreateStrategy(LoadBalancingAlgorithm algorithm)
        {
            return algorithm switch
            {
                LoadBalancingAlgorithm.LeastConnections => new LeastConnectionsStrategy(),
                LoadBalancingAlgorithm.RoundRobin => new RoundRobinStrategy(),
                LoadBalancingAlgorithm.Random => new RandomStrategy(),
                LoadBalancingAlgorithm.WeightedRoundRobin => new WeightedRoundRobinStrategy(),
                _ => throw new ArgumentException($"不支持的负载均衡算法: {algorithm}"),
            };
        }
    }
}
