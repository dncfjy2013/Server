using Server.Logger;
using Server.Proxy.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Test
{
    public class IP_ZONE
    {
        static ILogger logger = new LoggerInstance();
        public async static Task Main()
        {
            // 1. 初始化服务（使用自定义配置和日志）
            
            var options = new DefaultIpGeoLocationService.Options
            {
                CacheSize = 2000,
                CacheExpiry = TimeSpan.FromMinutes(15),
                EnableLogging = true
            };

            using var service = new DefaultIpGeoLocationService(options, logger);

            try
            {
                // 3. 添加自定义规则
                logger.LogInformation("Adding custom mappings...");
                AddCustomMappings(service);

                // 4. 从文件加载规则（如果存在）
                // 获取当前应用程序的基目录（即项目的bin / Debug或bin / Release目录）
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

                // 构建相对于应用程序基目录的配置文件路径
                const string configFileName = "ip-rules.txt";
                string configPath = Path.Combine(baseDirectory, configFileName);
                if (File.Exists(configPath))
                {
                    logger.LogInformation($"Loading rules from file: {configPath}");
                    service.LoadFromConfig(configPath);
                }
                else
                {
                    logger.LogWarning($"Config file not found: {configPath}");
                }

                // 5. 测试IP查询（同步）
                logger.LogInformation("\n=== Testing IP Queries (Sync) ===");
                TestIpQueries(service);

                // 6. 测试IP查询（异步/并行）
                logger.LogInformation("\n=== Testing IP Queries (Async/Parallel) ===");
                await TestIpQueriesAsync(service);

                // 7. 测试区域查询
                logger.LogInformation("\n=== Testing Zone Queries ===");
                TestZoneQueries(service);
            }
            catch (Exception ex)
            {
                logger.LogError($"Operation failed: {ex.Message}");
            }
        }

        static void AddCustomMappings(DefaultIpGeoLocationService service)
        {
            var customRules = new (string cidr, string zone)[]
            {
                // IPv4 rules
                ("103.21.0.0/13", "cloudflare"),
                ("202.96.0.0/11", "telecom-cn"),
                ("221.130.0.0/16", "unicom-cn"),
                
                // IPv6 rules
                ("2606:4700::/32", "cloudflare"),
                ("240e:0:0:0::/20", "telecom-cn"),
                ("2408:0:0:0::/20", "unicom-cn"),
                
                // Special cases
                ("192.0.2.0/24", "example-net"), // RFC 5737
                ("2001:db8::/32", "example-net")  // RFC 3849
            };

            foreach (var (cidr, zone) in customRules)
            {
                if (service.TryAddMapping(cidr, zone))
                {
                    logger.LogDebug($"Added rule: {cidr} → {zone}");
                }
                else
                {
                    logger.LogWarning($"Failed to add rule: {cidr}");
                }
            }
        }

        static void TestIpQueries(DefaultIpGeoLocationService service)
        {
            var testIps = new[]
            {
                // IPv4 tests
                "192.168.1.5",      // 内网 (local)
                "103.21.128.45",    // cloudflare
                "202.96.134.133",   // telecom-cn
                "221.130.255.255",  // unicom-cn
                "192.0.2.1",        // example-net
                
                // IPv6 tests
                "2606:4700:4700::1111", // cloudflare
                "240e:0:0:0:1234:5678:9abc:def0", // telecom-cn
                "2408:1:2:3:4:5:6:7",    // unicom-cn
                "2001:db8:1234:5678::1", // example-net
                "fc00:abcd::1",          // 内网 (local)
                
                // 无效测试
                "invalid-ip",
                "192.168.1.5/32",
                "2001:db8::/32"
            };

            foreach (var ip in testIps)
            {
                var zone = service.GetZoneByIp(ip);
                logger.LogInformation($"IP: {ip,-30} → Zone: {zone}");
            }
        }

        static async Task TestIpQueriesAsync(DefaultIpGeoLocationService service)
        {
            var testIps = new[]
            {
                "8.8.8.8",           // Google DNS
                "2001:4860:4860::8888", // Google DNS IPv6
                "1.1.1.1",           // Cloudflare DNS
                "2606:4700:4700::1111", // Cloudflare DNS IPv6
                "9.9.9.9",           // Quad9 DNS
                "2620:fe::fe",       // Quad9 DNS IPv6
            };

            var tasks = testIps.Select(ip => Task.Run(() =>
            {
                var zone = service.GetZoneByIp(ip);
                logger.LogInformation($"[ASYNC] IP: {ip,-25} → Zone: {zone}");
                return zone;
            }));

            await Task.WhenAll(tasks);
        }

        static void TestZoneQueries(DefaultIpGeoLocationService service)
        {
            var testZones = new[]
            {
                "local",
                "cloudflare",
                "telecom-cn",
                "non-existent-zone"
            };

            foreach (var zone in testZones)
            {
                var cidrs = service.GetIpRangesByZone(zone);
                logger.LogInformation($"Zone: {zone} ({cidrs.Count} CIDRs)");

                foreach (var cidr in cidrs)
                {
                    logger.LogDebug($"  - {cidr}");
                }
            }
        }
    }
}
