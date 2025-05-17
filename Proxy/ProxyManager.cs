using Server.Logger;
using Server.Proxy.Config;
using Server.Proxy.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Server.Proxy.Common
{
    /// <summary>
    /// 端口转发管理器 - 封装高级端口转发器的配置与生命周期管理
    /// </summary>
    public sealed class PortForwardingManager : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly AdvancedPortForwarder _forwarder;
        private List<EndpointConfig> _endpointConfigs = new();
        private bool _isRunning = false;

        public PortForwardingManager(ILogger logger)
        {
            _logger = logger;
            _forwarder = new AdvancedPortForwarder(logger);
        }

        #region TCP 配置方法
        /// <summary>
        /// 添加TCP端口转发配置
        /// </summary>
        public PortForwardingManager AddTcpForwarding(
            int listenPort,
            List<(string ip, int port)> targetServers,  // 多个目标服务器
            string listenIp = "0.0.0.0",
            int maxConnections = 1000)
        {
            _endpointConfigs.Add(new EndpointConfig
            {
                ListenIp = listenIp,
                ListenPort = listenPort,
                Protocol = ConnectType.Tcp,
                TargetServers = targetServers.Select(t =>
                    new TargetServer(t.ip, listenPort, t.port, _forwarder.GetZoneByIP(t.ip))).ToList(),
                MaxConnections = maxConnections
            });

            _logger.LogInformation($"添加TCP转发配置: {listenIp}:{listenPort} → 多目标服务器");
            return this;
        }
        #endregion

        #region SSL/TCP 多目标配置
        public PortForwardingManager AddSslTcpForwarding(
            int listenPort,
            List<(string ip, int port)> targetServers,  // 多目标服务器列表
            X509Certificate2 serverCertificate,
            bool requireClientCertificate = false,
            string listenIp = "0.0.0.0",
            int maxConnections = 1000)
        {
            _endpointConfigs.Add(new EndpointConfig
            {
                ListenIp = listenIp,
                ListenPort = listenPort,
                Protocol = ConnectType.SslTcp,
                ServerCertificate = serverCertificate,
                ClientCertificateRequired = requireClientCertificate,
                TargetServers = targetServers.Select(t => new TargetServer(t.ip, listenPort, t.port, _forwarder.GetZoneByIP(t.ip))
                {
                    BackendProtocol = ConnectType.SslTcp  // 目标服务器使用SSL协议
                }).ToList(),
                MaxConnections = maxConnections
            });

            _logger.LogInformation($"添加SSL/TCP多目标转发: {listenIp}:{listenPort} → {string.Join(", ", targetServers)}");
            return this;
        }
        #endregion

        #region UDP 多目标配置
        public PortForwardingManager AddUdpForwarding(
            int listenPort,
            List<(string ip, int port)> targetServers,  // 多目标服务器列表
            string listenIp = "0.0.0.0",
            int maxConnections = 1000)
        {
            _endpointConfigs.Add(new EndpointConfig
            {
                ListenIp = listenIp,
                ListenPort = listenPort,
                Protocol = ConnectType.Udp,
                TargetServers = targetServers.Select(t =>
                    new TargetServer(t.ip, listenPort, t.port, _forwarder.GetZoneByIP(t.ip))).ToList(),
                MaxConnections = maxConnections
            });

            _logger.LogInformation($"添加UDP多目标转发: {listenIp}:{listenPort} → {string.Join(", ", targetServers)}");
            return this;
        }
        #endregion

        #region HTTP 多目标配置
        public PortForwardingManager AddHttpForwarding(
            int listenPort,
            List<(string host, int port)> targetServers,  // 多目标服务器列表（支持域名/IP）
            string listenIp = "0.0.0.0",
            int maxConnections = 1000)
        {
            _endpointConfigs.Add(new EndpointConfig
            {
                ListenIp = listenIp,
                ListenPort = listenPort,
                Protocol = ConnectType.Http,
                TargetServers = targetServers.Select(t =>
                    new TargetServer(t.host, listenPort, t.port, _forwarder.GetZoneByIP(t.host))).ToList(),
                MaxConnections = maxConnections
            });

            _logger.LogInformation($"添加HTTP多目标转发: {listenIp}:{listenPort} → {string.Join(", ", targetServers)}");
            return this;
        }
        #endregion

        #region 生命周期管理
        /// <summary>
        /// 启动所有配置的端口转发
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
            {
                _logger.LogWarning("转发管理器已在运行中");
                return;
            }

            ValidateEndpointConfigs();

            _forwarder.Init(_endpointConfigs);

            _logger.LogInformation($"启动端口转发管理器，配置数: {_endpointConfigs.Count}");
            await _forwarder.StartAsync();
            _isRunning = true;
        }

        /// <summary>
        /// 停止所有端口转发
        /// </summary>
        public async Task StopAsync(TimeSpan timeout)
        {
            if (!_isRunning)
            {
                _logger.LogWarning("转发管理器未在运行中");
                return;
            }

            _logger.LogInformation("停止端口转发管理器");
            await _forwarder.StopAsync(timeout);
            _isRunning = false;
        }

        /// <summary>
        /// 获取当前转发器性能指标
        /// </summary>
        public PortForwarderMetrics GetMetrics()
        {
            return _forwarder.GetMetrics();
        }

        /// <summary>
        /// 异步释放资源
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_isRunning)
            {
                await StopAsync(TimeSpan.FromSeconds(30));
            }

            await _forwarder.DisposeAsync();
        }
        #endregion

        #region 私有方法
        /// <summary>
        /// 验证端点配置的有效性
        /// </summary>
        private void ValidateEndpointConfigs()
        {
            if (_endpointConfigs.Count == 0)
            {
                throw new InvalidOperationException("没有配置任何转发端点");
            }

            // 检查端口冲突
            var usedPorts = new HashSet<int>();
            foreach (var config in _endpointConfigs)
            {
                if (!usedPorts.Add(config.ListenPort))
                {
                    throw new InvalidOperationException($"检测到端口冲突: 多个配置使用了端口 {config.ListenPort}");
                }

                // 验证SSL配置
                if (config.Protocol == ConnectType.SslTcp && config.ServerCertificate == null)
                {
                    throw new InvalidOperationException($"SSL/TCP配置需要提供服务器证书: 端口 {config.ListenPort}");
                }
            }
        }
        #endregion
    }
}