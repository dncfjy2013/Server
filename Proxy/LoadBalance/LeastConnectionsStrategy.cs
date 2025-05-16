using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance
{
    // 最小连接数策略（原实现）
    public class LeastConnectionsStrategy : ILoadBalancingStrategy
    {
        public TargetServer SelectServer(List<TargetServer> servers)
        {
            var minServer = servers.MinBy(s => s.CurrentConnections);
            if (minServer == null)
            {
                throw new InvalidOperationException("目标服务器列表为空，无法转发请求");
            }
            return minServer;
        }
    }
}
