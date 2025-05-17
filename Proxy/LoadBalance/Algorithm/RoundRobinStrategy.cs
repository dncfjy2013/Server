using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance.Algorithm
{
    // 轮询策略
    public class RoundRobinStrategy : ILoadBalancingStrategy
    {
        private int _currentIndex = 0;
        private readonly object _lock = new();

        public TargetServer SelectServer(List<TargetServer> servers, object obj = null)
        {
            if (!servers.Any())
            {
                throw new InvalidOperationException("目标服务器列表为空，无法转发请求");
            }

            lock (_lock)
            {
                var selected = servers[_currentIndex];
                _currentIndex = (_currentIndex + 1) % servers.Count;
                return selected;
            }
        }
    }
}
