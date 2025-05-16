using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance
{
    // 加权轮询策略（基于权重的轮询）
    public class WeightedRoundRobinStrategy : ILoadBalancingStrategy
    {
        private int _currentIndex = 0;
        private int _currentWeight = 0;
        private int _gcdValue = 0; // 最大公约数
        private int _maxWeight = 0; // 最大权重
        private readonly object _lock = new();

        public TargetServer SelectServer(List<TargetServer> servers)
        {
            if (!servers.Any())
            {
                throw new InvalidOperationException("目标服务器列表为空，无法转发请求");
            }

            // 初始化权重参数
            if (_gcdValue == 0)
            {
                InitializeWeights(servers);
            }

            lock (_lock)
            {
                while (true)
                {
                    _currentIndex = (_currentIndex + 1) % servers.Count;
                    if (_currentIndex == 0)
                    {
                        _currentWeight = _currentWeight - _gcdValue;
                        if (_currentWeight <= 0)
                        {
                            _currentWeight = _maxWeight;
                            if (_currentWeight == 0)
                            {
                                return null; // 所有服务器权重为0
                            }
                        }
                    }

                    var server = servers[_currentIndex];
                    if (server.Weight >= _currentWeight)
                    {
                        return server;
                    }
                }
            }
        }

        private void InitializeWeights(List<TargetServer> servers)
        {
            _maxWeight = servers.Max(s => s.Weight);
            _gcdValue = CalculateGCD(servers.Select(s => s.Weight).ToList());
        }

        // 计算最大公约数
        private int CalculateGCD(List<int> weights)
        {
            if (weights == null || !weights.Any())
            {
                return 0;
            }

            int result = weights[0];
            for (int i = 1; i < weights.Count; i++)
            {
                result = GCD(result, weights[i]);
            }
            return result;
        }

        private int GCD(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }
    }
}
