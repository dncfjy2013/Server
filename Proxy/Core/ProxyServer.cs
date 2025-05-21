using Server.Logger;
using Server.Proxy.Common;
using Server.Proxy.Config;
using Server.Proxy.LoadBalance;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    sealed class AdvancedPortForwarder : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private List<EndpointConfig> _endpoints;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _isRunning;
        private bool _disposed;

        // 协议专属转发器
        private readonly TcpPortForwarder _tcpForwarder;
        private readonly UdpPortForwarder _udpForwarder;
        private readonly HttpPortForwarder _httpForwarder;

        // 新增 IP 相关成员
        private readonly DefaultIpGeoLocationService _ipGeoLocationService;
        private readonly DefaultIpGeoLocationService.Options _ipOptions = new()
        {
            CacheSize = 2000,
            CacheExpiry = TimeSpan.FromMinutes(15),
            EnableLogging = true
        };

        public AdvancedPortForwarder(ILogger logger, ILoadBalancer loadBalancer)
        {
            _logger = logger;
            _tcpForwarder = new TcpPortForwarder(logger, loadBalancer);
            _udpForwarder = new UdpPortForwarder(logger, loadBalancer);
            _httpForwarder = new HttpPortForwarder(logger, loadBalancer);

            // 初始化 IP 策略（主控制器统一管理）
            _ipGeoLocationService = new DefaultIpGeoLocationService(_ipOptions, logger);
            InitIpZone();
        }

        private void InitIpZone()
        {
            _logger.LogInformation("初始化 IP 区域策略...");
            AddCustomMappings(_ipGeoLocationService);
            LoadRulesFromFile(_ipGeoLocationService);
        }

        private void AddCustomMappings(DefaultIpGeoLocationService service)
        {
            var customRules = new (string cidr, string zone)[]
            {
            // IPv4 规则
            ("103.21.0.0/13", "cloudflare"),
            ("202.96.0.0/11", "telecom-cn"),
            ("221.130.0.0/16", "unicom-cn"),
            
            // IPv6 规则
            ("2606:4700::/32", "cloudflare"),
            ("240e:0:0:0::/20", "telecom-cn"),
            ("2408:0:0:0::/20", "unicom-cn"),
            
            // 特殊案例
            ("192.0.2.0/24", "example-net"), // RFC 5737
            ("2001:db8::/32", "example-net")  // RFC 3849
            };

            foreach (var (cidr, zone) in customRules)
            {
                if (service.TryAddMapping(cidr, zone))
                {
                    _logger.LogDebug($"添加 IP 规则：{cidr} → {zone}");
                }
                else
                {
                    _logger.LogWarning($"添加 IP 规则失败：{cidr}");
                }
            }
        }

        private void LoadRulesFromFile(DefaultIpGeoLocationService service)
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ip-rules.txt");
            if (File.Exists(configPath))
            {
                _logger.LogInformation($"从文件加载 IP 规则：{configPath}");
                service.LoadFromConfig(configPath);
            }
            else
            {
                _logger.LogWarning($"未找到 IP 规则文件：{configPath}");
            }
        }

        // 公开 IP 解析方法供各协议调用
        public string GetZoneByIp(string ipAddress)
        {
            return _ipGeoLocationService.GetZoneByIp(ipAddress);
        }

        public async Task StartAsync()
        {
            if (_isRunning) throw new InvalidOperationException("转发器已运行");
            _isRunning = true;
            _logger.LogInformation("启动高级端口转发器...");

            var tasks = new List<Task>
            {
                _tcpForwarder.StartAsync(_endpoints),
                _udpForwarder.StartAsync(_endpoints),
                _httpForwarder.StartAsync(_endpoints)
            };

            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { _logger.LogInformation("转发器停止请求已接收"); }
            finally { _isRunning = false; }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            _logger.LogInformation("开始优雅停止转发器...");
            _cancellationTokenSource.Cancel();

            await Task.WhenAll(
                _tcpForwarder.StopAsync(timeout),
                _udpForwarder.StopAsync(timeout),
                _httpForwarder.StopAsync(timeout)
            );

            _logger.LogInformation("所有协议转发器已停止");
        }

        public PortForwarderMetrics GetMetrics()
        {
            var tcpMetrics = _tcpForwarder.GetMetrics();
            var udpMetrics = _udpForwarder.GetMetrics();
            var httpMetrics = _httpForwarder.GetMetrics();

            return new PortForwarderMetrics
            {
                ActiveConnections = tcpMetrics.ActiveConnections + udpMetrics.ActiveConnections + httpMetrics.ActiveConnections,
                ConnectionMetrics = tcpMetrics.ConnectionMetrics
                    .Concat(udpMetrics.ConnectionMetrics)
                    .Concat(httpMetrics.ConnectionMetrics)
                    .ToList(),
                EndpointStatus = tcpMetrics.EndpointStatus
                    .Concat(udpMetrics.EndpointStatus)
                    .Concat(httpMetrics.EndpointStatus)
                    .ToList()
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await StopAsync(TimeSpan.FromSeconds(30));
                _cancellationTokenSource.Dispose();
                await _tcpForwarder.DisposeAsync();
                await _udpForwarder.DisposeAsync();
                await _httpForwarder.DisposeAsync();
            }
        }
    }
}