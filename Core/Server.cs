using Server.Extend;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Server.Core
{
    partial class Server
    {
        private readonly int _port;
        private readonly int _sslPort;
        private X509Certificate2 _serverCert;
        private readonly TrafficMonitor _trafficMonitor;
        Logger logger = new Logger();
        private readonly Timer _heartbeatTimer;
        private readonly Timer _trafficMonitorTimer;
        private int _monitorInterval = 5000; // 默认监控间隔
        private bool _isRunning;
        private readonly int HeartbeatInterval = 10000;
        private Socket _listener;
        private readonly int ListenMax = 100;
        private TcpListener _sslListener;
        private readonly CancellationTokenSource _cts = new();
        private readonly object _lock = new(); // 用于线程安全日志记录



        public Server(int port, int sslPort, string certPath = null)
        {
            _port = port;
            _sslPort = sslPort;

            // 加载SSL证书
            if (!string.IsNullOrEmpty(certPath))
            {
                _serverCert = new X509Certificate2(certPath);
                logger.LogWarning("SSL is not verified");
            }

            // 初始化流量监控器（关键修改）
            _trafficMonitor = new TrafficMonitor(_clients, _monitorInterval);

            _heartbeatTimer = new Timer(_ => CheckHeartbeats(), null, Timeout.Infinite, Timeout.Infinite);
            _trafficMonitorTimer = new Timer(_ => _trafficMonitor.Monitor(), null, Timeout.Infinite, Timeout.Infinite);

            logger.LogInformation("Start Sever");

            StartProcessing();
        }

        public void SetMonitorInterval(int interval)
        {
            lock (_lock)
            {
                _monitorInterval = interval;
                _trafficMonitorTimer.Change(interval, interval);
            }
        }

        public void Start(bool enableMonitoring = false)
        {
            _isRunning = true;
            _heartbeatTimer.Change(0, HeartbeatInterval);

            if (enableMonitoring)
            {
                _trafficMonitor.ModifyEnable(true);
                _trafficMonitorTimer.Change(0, _monitorInterval);
            }

            // 启动普通端口监听
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
            _listener.Listen(ListenMax);
            logger.LogInformation($"Socket started on port {_port} with monitoring {(enableMonitoring ? "enabled" : "disabled")}.");

            // 启动SSL端口监听
            if (_serverCert != null)
            {
                _sslListener = new TcpListener(IPAddress.Any, _sslPort);
                _sslListener.Start();
                logger.LogInformation($"SSL started on port {_port} with monitoring {(enableMonitoring ? "enabled" : "disabled")}.");
                _ = AcceptSslClients();
            }

            AcceptSocketClients();
        }

        public void Stop()
        {
            _cts.Cancel();
            _processingCts.Cancel();
            _isRunning = false;
            _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _trafficMonitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _listener?.Dispose();

            foreach (var client in _clients.Values)
            {
                DisconnectClient(client.Id);
            }

            logger.LogCritical("Server stopped.");

            logger.Dispose();
        }
    }
}