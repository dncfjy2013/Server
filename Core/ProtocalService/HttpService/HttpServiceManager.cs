using Logger;
using Server.Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.ProtocalService.HttpService
{
    // 服务管理器
    public class HttpServiceManager
    {
        private readonly ILogger _logger;
        private readonly RequestHandler _requestHandler;
        private readonly ConnectionManager _connectionManager;
        private readonly List<IHttpService> _services = new();
        private bool _isRunning;
        private uint _nextClientId;
        private readonly List<string> _hosts;
        private readonly Dictionary<string, ServiceConfig> _serviceConfigs = new();

        // 服务配置枚举
        public enum ServiceType { Http, Https }

        // 服务配置类
        public class ServiceConfig
        {
            public ServiceType Type { get; set; }
            public string CertificatePath { get; set; }
            public string CertificatePassword { get; set; }
            public string TrustedCertPath { get; set; }
        }

        public bool IsRunning => _isRunning;

        public HttpServiceManager(ILogger logger, ref uint nextClientId, List<string> hosts, ConnectionManager connectionManager)
        {
            _nextClientId = nextClientId;
            _logger = logger;
            _hosts = hosts ?? new List<string>();
            _connectionManager = connectionManager;
            _requestHandler = new RequestHandler(_logger, _connectionManager);
        }

        // 注册服务配置（支持按主机单独配置）
        public void RegisterServiceConfig(string host, ServiceType type, string certPath = null,
            string certPassword = null, string trustedCertPath = null)
        {
            if (string.IsNullOrEmpty(host))
                throw new ArgumentException("Host cannot be null or empty", nameof(host));

            _serviceConfigs[host] = new ServiceConfig
            {
                Type = type,
                CertificatePath = certPath,
                CertificatePassword = certPassword,
                TrustedCertPath = trustedCertPath
            };
            _logger.LogDebug($"Service config registered: {host} ({type})");
        }

        // 批量创建服务（根据_hosts列表和配置）
        private void CreateServicesFromHosts()
        {
            if (_hosts.Count == 0)
            {
                _logger.LogWarning("Host list is empty, no services will be created");
                return;
            }

            foreach (var host in _hosts)
            {
                if (string.IsNullOrEmpty(host))
                {
                    _logger.LogWarning("Host cannot be null or empty, skipping");
                    continue;
                }

                // 自动解析协议类型（http/https）
                ServiceType type;
                if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    type = ServiceType.Https;
                }
                else if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    type = ServiceType.Http;
                }
                else
                {
                    // 无协议前缀默认使用HTTP
                    type = ServiceType.Http;
                    _logger.LogWarning($"Host {host} has no protocol prefix, defaulting to HTTP");
                }

                // 从配置中获取证书信息（若存在）
                ServiceConfig config = null;
                if (_serviceConfigs.TryGetValue(host, out var configFromDict))
                {
                    config = configFromDict;
                    // 配置类型优先于自动解析类型
                    type = config.Type;
                    _logger.LogDebug($"Using configured type {type} for host {host}");
                }
                else
                {
                    // 未配置时使用自动解析的类型
                    config = new ServiceConfig { Type = type };
                }

                switch (type)
                {
                    case ServiceType.Http:
                        AddHttpService(host);
                        break;
                    case ServiceType.Https:
                        // 校验HTTPS必需参数
                        if (string.IsNullOrEmpty(config.CertificatePath))
                        {
                            throw new InvalidOperationException($"HTTPS service requires certificate path for host: {host}");
                        }
                        AddHttpsService(
                            host,
                            config.CertificatePath,
                            config.CertificatePassword ?? "", // 允许密码为空
                            config.TrustedCertPath
                        );
                        break;
                    default:
                        _logger.LogWarning($"Unknown service type {type} for host: {host}, using HTTP");
                        AddHttpService(host);
                        break;
                }
            }
        }

        // 创建并添加HTTP服务
        public void AddHttpService(string host)
        {
            var service = new HttpService(_logger, host, ref _nextClientId, _requestHandler, _connectionManager);
            _services.Add(service);
            _logger.LogDebug($"HTTP Service added: {host}");
        }

        // 创建并添加HTTPS服务
        public void AddHttpsService(string host, string certificatePath, string certificatePassword, string trustedCertPath = null)
        {
            var service = new HttpsService(_logger, host, ref _nextClientId, certificatePath, certificatePassword, trustedCertPath, _requestHandler, _connectionManager);
            _services.Add(service);
            _logger.LogDebug($"HTTPS Service added: {host}");
        }

        public void StartAll()
        {
            if (_isRunning) return;

            try
            {
                // 先创建服务（若未创建）
                CreateServicesFromHosts();

                foreach (var service in _services)
                {
                    service.Start();
                }
                _isRunning = true;
                _logger.LogInformation($"All HTTP services started on {string.Join(", ", _hosts)}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Failed to start services: {ex.Message}");
                throw;
            }
        }

        public void StopAll()
        {
            if (!_isRunning) return;

            try
            {
                foreach (var service in _services.Reverse<IHttpService>())
                {
                    service.Stop();
                }
                _isRunning = false;
                _logger.LogInformation($"All HTTP services stopped");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Failed to stop services: {ex.Message}");
                throw;
            }
        }
    }
}