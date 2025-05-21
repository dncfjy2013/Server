using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Common
{
    /// <summary>
    /// 连接类型枚举
    /// </summary>
    public enum ConnectType
    {
        Tcp,
        SslTcp,
        Udp,
        Http,
        Https
    }
}
