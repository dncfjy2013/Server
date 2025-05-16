using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.LoadBalance
{
    // 负载均衡策略接口
    public interface ILoadBalancingStrategy
    {
        TargetServer SelectServer(List<TargetServer> servers);
    }
}
