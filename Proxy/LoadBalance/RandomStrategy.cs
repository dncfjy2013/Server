using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance
{
    // 随机策略
    public class RandomStrategy : ILoadBalancingStrategy
    {
        private readonly Random _random = new();

        public TargetServer SelectServer(List<TargetServer> servers)
        {
            if (!servers.Any())
            {
                throw new InvalidOperationException("目标服务器列表为空，无法转发请求");
            }

            return servers[_random.Next(0, servers.Count)];
        }
    }
}
