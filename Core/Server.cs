using Core.Message;
using Server.Core.Certification;
using Server.Core.Common;
using Server.Core.Extend;
using Server.Logger;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Server.Core
{
    public partial class ServerInstance
    {
        // 服务器监听的普通端口号，在构造函数中初始化，一旦初始化后不可更改
        private readonly int _port;
        // 服务器监听的 SSL 加密端口号，在构造函数中初始化，一旦初始化后不可更改
        private readonly int _sslPort;
        private readonly int _udpport;
        private readonly string _host;
        // 用于 SSL 连接的服务器证书对象，可通过构造函数传入的证书路径加载
        private X509Certificate2 _serverCert;
        // 流量监控器实例，用于监控服务器与客户端之间的流量情况，在构造函数中初始化
        private readonly TrafficMonitor _trafficMonitor;
        // 日志记录器实例，用于记录服务器运行过程中的各类信息，如错误、警告、信息等
        private ILogger _logger;
        // 心跳定时器，用于定期检查客户端的心跳情况，确保客户端连接正常
        private readonly Timer _heartbeatTimer;
        // 服务器运行状态标志，当为 true 时表示服务器正在运行，可接受客户端连接；为 false 时则停止服务
        private bool _isRunning;
        // 用于普通 TCP 连接的套接字监听器，负责监听普通端口的客户端连接请求
        private Socket _listener;
        // 用于 SSL 加密连接的 TCP 监听器，负责监听 SSL 端口的客户端连接请求
        private TcpListener _sslListener;
        // 用于 HttpListener 连接的监听器，负责监听 Http 端口的客户端连接请求
        private HttpListener _httpListener;
        // 用于 UdpClient 连接的监听器，负责监听 UDP 端口的客户端连接请求
        private UdpClient _udpListener;
        // 用于取消异步操作的取消令牌源，可在需要停止服务器时取消正在进行的异步任务
        public readonly CancellationTokenSource _cts = new();
        // 用于线程安全的日志记录操作的锁对象，确保在多线程环境下日志记录操作的线程安全性
        private readonly object _lock = new();

        private int _connectSocket, _connectSSL, _connectUDP, _connectHTTP;

        private ConnectionManager _ClientConnectionManager;

        private SSLManager _SSLManager;

        private MessageManager _messageManager;

        public ServerInstance(int port, int sslPort, int udpport, string host, X509Certificate2 certf = null)
        {
            _logger = LoggerInstance.Instance;
            _ClientConnectionManager = new ConnectionManager(_logger);
            _messageManager = new MessageManager(_clients, _logger);

            _logger.LogTrace($"Server constructor called with port: {port}, sslPort: {sslPort}, certf: {certf}");

            try
            {
                _port = port;
                _sslPort = sslPort;
                _udpport = udpport;
                _host = host;

                _logger.LogInformation("Server has begining initialization process.");

                // Information 等级：记录开始加载 SSL 证书的操作
                _logger.LogInformation("Initiating the loading of SSL certification.");
                if (certf != null)
                {
                    try
                    {
                        _serverCert = certf;
                        // Information 等级：记录 SSL 证书验证通过的信息
                        _logger.LogInformation("SSL certificate has been successfully verified.");
                    }
                    catch (Exception ex)
                    {
                        // Error 等级：记录加载 SSL 证书时出现的异常
                        _logger.LogError($"An error occurred while loading the SSL certificate from {certf}: {ex.Message}  ");
                    }
                }
                else
                {
                    // Critical 等级：记录没有提供 SSL 证书路径的严重情况
                    _logger.LogCritical("No SSL certification path was provided. Skipping SSL certificate loading.");
                }

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

                // Debug 等级：记录创建心跳定时器的操作
                _logger.LogDebug("Creating the heartbeat timer.");
                _heartbeatTimer = new Timer(_ => CheckHeartbeats(), null, Timeout.Infinite, Timeout.Infinite);
                
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
                lock (_lock)
                {
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
                }
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

                // 启动心跳定时器，属于关键操作步骤，使用Debug记录
                _logger.LogDebug($"Starting the heartbeat timer with an immediate start and interval of {ConstantsConfig.HeartbeatInterval} ms.");
                _heartbeatTimer.Change(0, ConstantsConfig.HeartbeatInterval);
                _logger.LogDebug("Heartbeat timer has been successfully started.");

                if (enableMonitoring)
                {
                    // 启用流量监控，属于功能开启操作，使用Debug记录
                    _logger.LogDebug($"Starting the traffic monitor timer with an immediate start and interval of {ConstantsConfig.MonitorInterval} ms.");
                    _trafficMonitor.ModifyEnable(true);
                }
                else
                    _logger.LogDebug($"No traffic monitor timer with an immediate start.");

                // 启动普通端口监听
                _logger.LogDebug($"Initiating the creation of a socket for port {_port}.");
                _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _logger.LogDebug($"Socket for port {_port} has been created.");

                _logger.LogDebug($"Binding the socket to port {_port}.");
                _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
                _logger.LogDebug($"Socket has been successfully bound to port {_port}.");

                _logger.LogDebug($"Starting to listen on port {_port} with a backlog of {ConstantsConfig.ListenMax}.");
                _listener.Listen(ConstantsConfig.ListenMax);
                _logger.LogInformation($"Socket server has started listening on port {_port}.");

                _logger.LogDebug($"Starting to create an UDP listener for port {_udpport}.");
                _udpListener = new UdpClient(_udpport);
                _logger.LogDebug($"UDP listener for port {_udpport} has been created");

                _logger.LogDebug($"Starting to create an HTTP listener.");
                _httpListener = new HttpListener();
                _logger.LogDebug($"HTTP listener has been created.");

                _logger.LogDebug($"Starting the HTTP listener on {_host}.");
                _httpListener.Prefixes.Add(_host);
                _httpListener.Start();
                _logger.LogInformation($"HTTP server has started listening on {_host}.");

                // 启动SSL端口监听
                if (_serverCert != null)
                {
                    _logger.LogDebug($"Starting to create an SSL listener for port {_sslPort}.");
                    _sslListener = new TcpListener(IPAddress.Any, _sslPort);
                    _logger.LogDebug($"SSL listener for port {_sslPort} has been created.");

                    _logger.LogDebug($"Starting the SSL listener on port {_sslPort}.");
                    _sslListener.Start();
                    _logger.LogInformation($"SSL server has started listening on port {_sslPort}.");

                    // 开始接受SSL客户端连接，属于关键操作步骤，使用Debug记录
                    _logger.LogDebug($"Starting to accept SSL clients on port {_sslPort}.");
                    AcceptSslClients();
                    _logger.LogDebug("Accepting SSL clients process has been initiated.");

                    _SSLManager = new SSLManager(_logger);
                }
                else
                {
                    // 没有SSL证书，无法启动SSL监听，使用Warning记录
                    _logger.LogWarning("No SSL certificate provided. SSL listener will not be started.");
                }

                // 开始接受普通套接字客户端连接，属于关键操作步骤，使用Debug记录
                _logger.LogDebug($"Starting to accept socket clients on port {_port}.");
                AcceptSocketClients();
                _logger.LogDebug("Accepting socket clients process has been initiated.");

                _logger.LogDebug($"Starting to accept udp clients on port {_udpport}.");
                AcceptUdpClients();
                _logger.LogDebug("Accepting udp clients process has been initiated.");

                _logger.LogDebug($"Starting to accept HTTP clients.");
                AcceptHttpClients();
                _logger.LogDebug("Accepting HTTP clients process has been initiated.");

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
                // 取消主取消令牌源，属于关键操作的开始步骤，使用Debug记录
                _logger.LogDebug("Canceling the primary cancellation token source _cts.");
                _cts.Cancel();
                _logger.LogDebug("Successfully canceled the primary cancellation token source _cts.");

                
                // 停止心跳定时器，属于系统定时任务的操作，使用Debug记录
                _logger.LogDebug("Halting the heartbeat timer by setting its interval to infinite.");
                _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _logger.LogDebug("The heartbeat timer has been successfully stopped.");

                // 停止流量监控定时器，也是系统定时任务的操作，使用Debug记录
                _logger.LogDebug("Stopping the traffic monitor timer.");
                _trafficMonitor.Dispose();
                _logger.LogDebug("The traffic monitor timer has been successfully stopped.");
                
                // 断开所有客户端连接，属于重要的资源清理操作，使用Information记录
                _logger.LogInformation("Initiating the disconnection process for all connected clients.");
                foreach (var client in _clients.Values)
                {
                    _logger.LogDebug($"Disconnecting client with ID {client.Id}.");
                    try
                    {
                        DisconnectClient(client.Id);
                        _logger.LogDebug($"Client with ID {client.Id} has been successfully disconnected.");
                    }
                    catch (Exception ex)
                    {
                        // 断开客户端连接时出现异常，使用Error记录
                        _logger.LogError($"An error occurred while disconnecting client {client.Id}: {ex.Message}");
                    }
                }
                _logger.LogInformation("All connected clients have been disconnected.");

                _logger.LogDebug("Disposing of the client state Manager.");
                _ClientConnectionManager.Dispose();
                _logger.LogDebug("The client state Manager has been disposed of.");

                // 处理套接字监听器的释放，根据是否存在监听器使用不同日志记录
                if (_listener != null)
                {
                    _logger.LogDebug("Disposing of the socket listener.");
                    _listener.Dispose();
                    _logger.LogDebug("The socket listener has been disposed of.");
                }
                else
                {
                    _logger.LogTrace("The socket listener is null, no disposal operation is required.");
                }

                if (_httpListener != null)
                {
                    _httpListener.Stop();
                    _httpListener.Close();
                    //_httpListener.Dispose();
                    _logger.LogDebug("HttpListener has been stopped and disposed.");
                }
                else
                {
                    _logger.LogTrace("The _httpListener listener is null, no disposal operation is required.");
                }

                if (_sslListener != null)
                {
                    _sslListener.Stop();
                    _logger.LogDebug("_sslListener has been stopped and disposed.");
                }
                else
                {
                    _logger.LogTrace("The _sslListener listener is null, no disposal operation is required.");
                }

                if (_udpListener != null)
                {
                    _udpListener.Close();
                    _logger.LogDebug("_udpListener has been stopped and disposed.");
                }
                else
                {
                    _logger.LogTrace("The _udpListener listener is null, no disposal operation is required.");
                }

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