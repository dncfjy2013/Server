using Server.Logger;
using Server.Proxy.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Server.Test
{
    public class Proxytest
    {
        public static async Task Main()
        {
            ILogger logger = LoggerInstance.Instance;
            X509Certificate2 serverCertificate = null; // 加载证书逻辑同原代码

            await using var manager = new PortForwardingManager(logger)

                // ---------------------- TCP 1对多 ----------------------
                .AddTcpForwarding(
                    listenPort: 1111,
                    targetServers: new List<(string, int)> {
                    ("192.168.12.10", 1111),  // 目标服务器1
                    ("192.168.12.11", 1111),  // 目标服务器2
                    ("192.168.12.12", 1111)   // 目标服务器3
                    })

                // ---------------------- SSL/TCP 1对多 ----------------------
                .AddSslTcpForwarding(
                    listenPort: 2222,
                    targetServers: new List<(string, int)> {
                    ("192.168.13.10", 2222),
                    ("192.168.13.11", 2222)
                    },
                    serverCertificate: serverCertificate!)  // 假设证书已加载

                // ---------------------- UDP 1对多 ----------------------
                .AddUdpForwarding(
                    listenPort: 3333,
                    targetServers: new List<(string, int)> {
                    ("192.168.12.15", 3333),
                    ("192.168.12.16", 3333)
                    })

                // ---------------------- HTTP 1对多 ----------------------
                .AddHttpForwarding(
                    listenPort: 5151,
                    targetServers: new List<(string, int)> {
                    ("www.exaed.com", 80),      // 目标域名1
                    ("api.exaed.com", 8080)     // 目标域名2
                    })
                ;

            await manager.StartAsync();
            await Task.Delay(TimeSpan.FromMinutes(5));
            await manager.StopAsync(TimeSpan.FromSeconds(10));
        }
    }
}
