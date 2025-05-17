using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance.Algorithm
{
    public class ZoneAffinityStrategy : ILoadBalancingStrategy
    {
        private readonly string _clientZone; // 客户端区域（如 "us-east-1"）

        public ZoneAffinityStrategy(string clientZone)
        {
            _clientZone = clientZone;
        }

        public TargetServer SelectServer(List<TargetServer> servers, object obj = null)
        {
            if (!servers.Any()) throw new InvalidOperationException("服务器列表为空");

            // 筛选同区域服务器
            var localServers = servers.Where(s => s.Zone == _clientZone).ToList();
            if (localServers.Any())
            {
                // 同区域内使用最小连接数策略
                return new LeastConnectionsStrategy().SelectServer(localServers);
            }
            // 无同区域服务器时回退到全局策略
            return new LeastConnectionsStrategy().SelectServer(servers);
        }
    }
}
