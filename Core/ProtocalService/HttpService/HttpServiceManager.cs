using Logger;
using Server.Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.ProtocalService.HttpService
{
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

        public enum ServiceType { Http, Https }

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
        }

        private void CreateServicesFromHosts()
        {
            if (_hosts.Count == 0)
            {
                _logger.Warn("Host list is empty, no services will be created");
                return;
            }

            foreach (var host in _hosts)
            {
                if (string.IsNullOrEmpty(host))
                {
                    _logger.Warn("Host cannot be null or empty, skipping");
                    continue;
                }

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
                    type = ServiceType.Http;
                    _logger.Warn($"Host {host} has no protocol prefix, defaulting to HTTP");
                }

                ServiceConfig config = null;
                if (_serviceConfigs.TryGetValue(host, out var configFromDict))
                {
                    config = configFromDict;
                    type = config.Type;
                }
                else
                {
                    config = new ServiceConfig { Type = type };
                }

                switch (type)
                {
                    case ServiceType.Http:
                        AddHttpService(host);
                        break;
                    case ServiceType.Https:
                        if (string.IsNullOrEmpty(config.CertificatePath))
                        {
                            throw new InvalidOperationException($"HTTPS service requires certificate path for host: {host}");
                        }
                        AddHttpsService(host, config.CertificatePath, config.CertificatePassword ?? "", config.TrustedCertPath);
                        break;
                    default:
                        _logger.Warn($"Unknown service type {type} for host: {host}, using HTTP");
                        AddHttpService(host);
                        break;
                }
            }
        }

        public void AddHttpService(string host)
        {
            var service = new HttpService(_logger, host, ref _nextClientId, _requestHandler, _connectionManager);
            _services.Add(service);
        }

        public void AddHttpsService(string host, string certificatePath, string certificatePassword, string trustedCertPath = null)
        {
            var service = new HttpsService(_logger, host, ref _nextClientId, certificatePath, certificatePassword, trustedCertPath, _requestHandler, _connectionManager);
            _services.Add(service);
        }

        public void StartAll()
        {
            if (_isRunning) return;

            try
            {
                CreateServicesFromHosts();

                foreach (var service in _services)
                {
                    service.Start();
                }
                _isRunning = true;
                _logger.Info("All HTTP services started");
            }
            catch (Exception ex)
            {
                _logger.Critical($"Failed to start services: {ex.Message}");
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
                _logger.Info("All HTTP services stopped");
            }
            catch (Exception ex)
            {
                _logger.Critical($"Failed to stop services: {ex.Message}");
                throw;
            }
        }
    }
}