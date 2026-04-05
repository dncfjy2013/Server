using Core.Message;
using Core.ProtocalService.HttpService;
using Core.ProtocalService.TcpService;
using Core.ProtocalService.UdpService;
using Logger;
using Server.Core.Certification;
using Server.Core.Common;
using Server.Core.Config;
using Server.Core.Extend;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Server.Core
{
    public class ServerInstance
    {
        private readonly TrafficMonitor _trafficMonitor;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<uint, ClientConfig> _clients = new();
        private readonly ConcurrentDictionary<uint, ClientConfig> _historyclients = new();
        private uint _nextClientId;
        private bool _isRunning;
        private int _connectSocket, _connectSSL, _connectUDP, _connectHTTP;
        private readonly ConnectionManager _clientConnectionManager;
        private readonly InMessage _inMessageManager;
        private readonly OutMessage _outMessageManager;
        private readonly TcpServiceInstance _tcpServiceInstance;
        private readonly UdpServiceInstance _udpServiceInstance;
        public readonly HttpServiceManager _httpServiceInstance;

        public ServerInstance(int port, int sslPort, int udpPort, List<string> host, X509Certificate2 certf = null)
        {
            _logger = LoggerInstance.Instance;

            try
            {
                _logger.Info("Server initializing...");

                _clientConnectionManager = new ConnectionManager(_logger);
                _outMessageManager = new OutMessage(_logger, _clients);
                _inMessageManager = new InMessage(_clients, _logger, _outMessageManager);
                _tcpServiceInstance = new TcpServiceInstance(port, sslPort, certf, ref _isRunning, _logger, _clientConnectionManager, _inMessageManager, _outMessageManager, ref _nextClientId, ref _connectSocket, ref _connectSSL, _clients, _historyclients);
                _udpServiceInstance = new UdpServiceInstance(_logger, udpPort, ref _nextClientId, _clientConnectionManager);
                _httpServiceInstance = new HttpServiceManager(_logger, ref _nextClientId, host, _clientConnectionManager);

                try
                {
                    _trafficMonitor = new TrafficMonitor(_clients, ConstantsConfig.MonitorInterval, _logger);
                    _logger.Info($"Traffic monitor initialized, interval: {ConstantsConfig.MonitorInterval}ms");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Traffic monitor init failed: {ex.Message}");
                }

                _logger.Info("Server initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.Critical($"Server initialization critical error: {ex.Message}");
            }
        }

        public void SetMonitorInterval(int interval)
        {
            try
            {
                var oldInterval = _trafficMonitor.GetMonitorInterval();
                if (_trafficMonitor.SetMonitorInterval(interval))
                {
                    _logger.Info($"Traffic monitor interval updated: {oldInterval}ms -> {interval}ms");
                }
                else
                {
                    _logger.Error($"Traffic monitor interval update failed: {oldInterval}ms -> {interval}ms");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Set monitor interval error: {ex.Message}");
            }
        }

        public void Start(bool enableMonitoring = false)
        {
            try
            {
                _isRunning = true;
                _logger.Info("Server started, accepting connections");

                if (enableMonitoring)
                {
                    _trafficMonitor.ModifyEnable(true);
                    _logger.Info("Traffic monitor enabled");
                }

                _logger.Info($"Server type: {(ConstantsConfig.IsUnityServer ? "Unity" : "Large")}");

                if (ConstantsConfig.IsUnityServer)
                {
                    _inMessageManager.Start();
                }
                _outMessageManager.Start();

                _tcpServiceInstance.Start();
                _udpServiceInstance.Start();
                _httpServiceInstance.StartAll();
            }
            catch (Exception ex)
            {
                _logger.Critical($"Server start critical error: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                _logger.Info("Server stopping...");

                _trafficMonitor.Dispose();
                _tcpServiceInstance.Stop();
                _udpServiceInstance.Stop();
                _httpServiceInstance.StopAll();

                _clientConnectionManager.Dispose();
                _inMessageManager?.Stop();
                _outMessageManager.Stop();

                _isRunning = false;
                _logger.Info("Server stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.Critical($"Server stop critical error: {ex.Message}");
            }

            try
            {
                _logger.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Error($"Logger dispose error: {ex.Message}");
            }
        }

        public List<int> GetConnectNum()
        {
            return new List<int> { _connectSocket, _connectSSL, _connectUDP, _connectHTTP };
        }
    }
}