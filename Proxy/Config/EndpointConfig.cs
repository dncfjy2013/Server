using Server.Proxy.Common;
using Server.Proxy.LoadBalance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Config
{
    /// <summary>
    /// 端点配置
    /// </summary>
    public class EndpointConfig
    {
        public string ListenIp { get; set; } = "0.0.0.0";
        public int ListenPort { get; set; }
        public ConnectType Protocol { get; set; }
        public List<TargetServer> TargetServers { get; set; } = new();
        public int MaxConnections { get; set; } = 1000;
        public bool ClientCertificateRequired { get; set; }
        public X509Certificate2 ServerCertificate { get; set; }
        public LoadBalancingAlgorithm LoadBalancingAlgorithm { get; set; } = LoadBalancingAlgorithm.LeastConnections;
    }
}
