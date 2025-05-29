using Core.Message;
using Core.ProtocalService.HttpService;
using Core.ProtocalService.TcpService;
using Core.ProtocalService.UdpService;
using Server.Core.Certification;
using Server.Core.Common;
using Server.Core.Config;
using Server.Core.Extend;
using Server.Logger;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Server.Core
{
    public partial class ServerInstance
    {
        // 流量监控器实例，用于监控服务器与客户端之间的流量情况，在构造函数中初始化
        private readonly TrafficMonitor _trafficMonitor;
        // 日志记录器实例，用于记录服务器运行过程中的各类信息，如错误、警告、信息等
        private ILogger _logger;
        /// <summary>
        /// 客户端连接字典（线程安全），键为客户端ID，值为客户端配置对象
        /// </summary>
        private readonly ConcurrentDictionary<uint, ClientConfig> _clients = new();
        private readonly ConcurrentDictionary<uint, ClientConfig> _historyclients = new();
        /// <summary>
        /// 客户端ID生成器（原子递增）
        /// </summary>
        private uint _nextClientId;
        // 服务器运行状态标志，当为 true 时表示服务器正在运行，可接受客户端连接；为 false 时则停止服务
        private bool _isRunning;
        private int _connectSocket, _connectSSL, _connectUDP, _connectHTTP;
        private ConnectionManager _ClientConnectionManager;
        private MessageManager _messageManager;
        private TcpServiceInstance _tcpServiceInstance;
        private UdpServiceInstance _udpServiceInstance;
        public HttpServiceInstance _HttpServiceInstance;

        public ServerInstance(int port, int sslPort, int udpport, string host, X509Certificate2 certf = null)
        {
            _logger = LoggerInstance.Instance;
            
            _logger.LogTrace($"Server constructor called with port: {port}, sslPort: {sslPort}, certf: {certf}");

            try
            {
                _logger.LogInformation("Server has begining initialization process.");

                _ClientConnectionManager = new ConnectionManager(_logger);
                _messageManager = new MessageManager(_clients, _logger);
                _tcpServiceInstance = new TcpServiceInstance(port, sslPort, certf, ref _isRunning, _logger, _ClientConnectionManager, _messageManager, ref _nextClientId, ref _connectSocket, ref _connectSSL, _clients, _historyclients);
                _udpServiceInstance = new UdpServiceInstance(ref _isRunning, _logger, udpport, ref _nextClientId, _ClientConnectionManager);
                _HttpServiceInstance = new HttpServiceInstance(_logger, ref _isRunning, host, ref _nextClientId, _ClientConnectionManager);

                // Information 等级：记录开始初始化流量监控器的操作
                _logger.LogInformation("Starting the initialization of the traffic monitor.");
                try
                {
                    _trafficMonitor = new TrafficMonitor(_clients, ConstantsConfig.MonitorInterval, _logger);
                    // Information 等级：记录流量监控器初始化成功及使用的监控间隔
                    _logger.LogInformation($"Traffic monitor has been initialized with an interval of {ConstantsConfig.MonitorInterval} ms.");
                }
                catch (Exception ex)
                {
                    // Error 等级：记录初始化流量监控器时出现的异常
                    _logger.LogError($"Failed to initialize the traffic monitor: {ex.Message}");
                }
                
                // Information 等级：记录服务器开始运行的信息
                _logger.LogInformation("Server initialized successed.");
            }
            catch (Exception ex)
            {
                // Critical 等级：记录服务器初始化过程中出现的严重异常
                _logger.LogCritical($"A critical error occurred during server initialization: {ex.Message}  ");
            }
        }

        public void SetMonitorInterval(int interval)
        {
            // Trace：方法调用细节（仅开发环境有用）
            _logger.LogTrace($"Enter SetMonitorInterval, new interval: {interval} ms");

            try
            {
                // Debug：关键操作开始（获取锁）
                _logger.LogDebug("Acquiring lock for thread-safe interval modification");

                double time = _trafficMonitor.GetMonitorInterval();

                // Trace：锁内变量检查（细粒度调试）
                _logger.LogTrace($"Current interval before update: {time} ms");

                // Information：配置变更通知（影响系统行为的操作）
                _logger.LogDebug($"Updating traffic monitor interval from {time} ms to {interval} ms");

                // Debug：定时器操作（关键功能调整）
                _logger.LogDebug($"Updating traffic monitor timer to interval: from {time} ms {interval} ms");
                if (!_trafficMonitor.SetMonitorInterval(interval))
                {
                    _logger.LogError($"traffic monitor timer to interval from {time} ms to {interval}");
                }
                else
                    _logger.LogCritical($"Traffic monitor interval changed from {time} ms to {interval} ms");
                
                // Debug：锁释放（线程安全相关）
                _logger.LogDebug("Lock released after interval modification");
            }
            catch (Exception ex)
            {
                // Error：可恢复的异常（如无效的时间间隔）
                _logger.LogError($"Failed to set monitor interval: {ex.Message}");
            }
        }

        public void Start(bool enableMonitoring = false)
        {
            _logger.LogTrace($"Start method invoked with enableMonitoring set to {enableMonitoring}");

            try
            {
                // 设置服务器运行状态为开启，属于重要状态变更，使用Information记录
                _logger.LogInformation("Server is now accepting connections.");
                _isRunning = true;

                if (enableMonitoring)
                {
                    // 启用流量监控，属于功能开启操作，使用Debug记录
                    _logger.LogDebug($"Starting the traffic monitor timer with an immediate start and interval of {ConstantsConfig.MonitorInterval} ms.");
                    _trafficMonitor.ModifyEnable(true);
                }
                else
                    _logger.LogDebug($"No traffic monitor timer with an immediate start.");

                _logger.LogCritical($"Server start with type as {(ConstantsConfig.IsUnityServer? "unity" : "large")} ");

                if (ConstantsConfig.IsUnityServer)
                {
                    // Trace 等级：记录开始消息处理的详细信息
                    _logger.LogTrace("Commencing Incoming message processing.");
                    _messageManager.StartInComingProcessing();
                    // Trace 等级：记录消息处理已成功开始的详细信息
                    _logger.LogTrace("Incoming Message processing has been successfully started.");
                }
                // Trace 等级：记录开始消息处理的详细信息
                _logger.LogTrace("Commencing Outcoming message processing.");
                _messageManager.StartOutgoingMessageProcessing();
                // Trace 等级：记录消息处理已成功开始的详细信息
                _logger.LogTrace("Outcoming Message processing has been successfully started.");

                _tcpServiceInstance.Start();
                _udpServiceInstance.Start();
                _HttpServiceInstance.Start();
            }
            catch (Exception ex)
            {
                // 启动过程中出现异常，使用Critical记录
                _logger.LogCritical($"A critical error occurred while starting the server: {ex.Message} ");
            }
        }

        public void Stop()
        {
            _logger.LogTrace("Stop method has been invoked. Initiating the server shutdown sequence.");

            try
            {
                // 停止流量监控定时器，也是系统定时任务的操作，使用Debug记录
                _logger.LogDebug("Stopping the traffic monitor timer.");
                _trafficMonitor.Dispose();
                _logger.LogDebug("The traffic monitor timer has been successfully stopped.");

                _tcpServiceInstance.Stop();
                _udpServiceInstance.Stop();
                _HttpServiceInstance.Stop();

                _logger.LogDebug("Disposing of the client state Manager.");
                _ClientConnectionManager.Dispose();
                _logger.LogDebug("The client state Manager has been disposed of.");

                _messageManager.ShutDown();
            }
            catch (Exception ex)
            {
                // 整个停止过程中出现异常，使用Critical记录
                _logger.LogCritical($"A critical error occurred during server shutdown: {ex.Message}");
            }

            _isRunning = false;
            // 服务器停止，是重要的系统状态变更，使用Critical记录
            _logger.LogCritical("Server has been stopped.");

            try
            {
                // 释放日志记录器，属于资源清理操作，使用Debug记录
                _logger.LogDebug("Disposing of the _logger.");
                _logger.Dispose();
                _logger.LogDebug("The _logger has been disposed of.");
            }
            catch (Exception ex)
            {
                // 释放日志记录器时出现异常，使用Error记录
                _logger.LogError($"An error occurred while disposing of the _logger: {ex.Message}");
            }
        }

        public List<int> GetConnectNum()
        {
            List<int> re = new List<int>() { _connectSocket, _connectSSL, _connectUDP, _connectHTTP };
            return re;
        }
    }
}