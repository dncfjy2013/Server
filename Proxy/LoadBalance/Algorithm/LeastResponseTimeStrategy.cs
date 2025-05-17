using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance.Algorithm
{
    public class LeastResponseTimeStrategy : ILoadBalancingStrategy
    {
        public TargetServer SelectServer(List<TargetServer> servers, object obj = null)
        {
            if (!servers.Any()) throw new InvalidOperationException("服务器列表为空");

            // 按平均响应时间升序排序，取最小值
            return servers.MinBy(s => s.AverageResponseTimeMs)
                ?? throw new InvalidOperationException("无法获取有效服务器");
        }
    }
}
