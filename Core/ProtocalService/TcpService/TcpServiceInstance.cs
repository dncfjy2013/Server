using Core.Message;
using Logger;
using MySqlX.XDevAPI;
using Protocol;
using Server.Core.Certification;
using Server.Core.Common;
using Server.Core.Config;
using Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Core.ProtocalService.TcpService
{
    public class TcpServiceInstance
    {
        // 服务器监听的普通端口号，在构造函数中初始化，一旦初始化后不可更改
        private readonly int _port;
        // 服务器监听的 SSL 加密端口号，在构造函数中初始化，一旦初始化后不可更改
        private readonly int _sslPort;
        // 用于 SSL 连接的服务器证书对象，可通过构造函数传入的证书路径加载
        private X509Certificate2 _serverCert;
        // 用于普通 TCP 连接的套接字监听器，负责监听普通端口的客户端连接请求
        private Socket _listener;
        // 用于 SSL 加密连接的 TCP 监听器，负责监听 SSL 端口的客户端连接请求
        private TcpListener _sslListener;
        // 服务器运行状态标志，当为 true 时表示服务器正在运行，可接受客户端连接；为 false 时则停止服务
        private bool _isRunning;

        private ILogger _logger;
        private SSLManager _SSLManager;
        private ConnectionManager _ClientConnectionManager;
        private InMessage _InmessageManager;
        private OutMessage _outMessageManager;
        /// <summary>
        /// 客户端ID生成器（原子递增）
        /// </summary>
        private uint _nextClientId;
        private int _connectSocket, _connectSSL;
        private readonly ConcurrentDictionary<uint, ClientConfig> _clients = new();
        // 一个布尔类型的标志，用于控制是否允许实时数据功能
        private bool _isRealTimeTransferAllowed = false;

        // 一个布尔类型的标志，用于控制是否继续接收新的数据
        // 当设置为 true 时，服务器会继续接收新的客户端消息；设置为 false 时，停止接收新消息
        private bool _isReceiving = true;
        private readonly ConcurrentDictionary<uint, ClientConfig> _historyclients = new();
        // 心跳定时器，用于定期检查客户端的心跳情况，确保客户端连接正常
        private readonly Timer _heartbeatTimer;

        public TcpServiceInstance(int port, int sslPort, X509Certificate2 serverCert, ref bool isRunning, ILogger logger, ConnectionManager clientConnectionManager, InMessage messageManager, OutMessage outMessage, ref uint nextClientId, ref int connectSocket, ref int connectSSL, ConcurrentDictionary<uint, ClientConfig> clients, ConcurrentDictionary<uint, ClientConfig> historyclients)
        {
            _port = port;
            _sslPort = sslPort;
            _serverCert = serverCert;
            _isRunning = isRunning;
            _logger = logger;
            _ClientConnectionManager = clientConnectionManager;
            _InmessageManager = messageManager;
            _nextClientId = nextClientId;
            _connectSocket = connectSocket;
            _connectSSL = connectSSL;
            _clients = clients;
            _historyclients = historyclients;
            _outMessageManager = outMessage;

            // Debug 等级：记录创建心跳定时器的操作
            _logger.LogDebug("Creating the heartbeat timer.");
            _heartbeatTimer = new Timer(_ => CheckHeartbeats(), null, Timeout.Infinite, Timeout.Infinite);


            // 启动心跳定时器，属于关键操作步骤，使用Debug记录
            _logger.LogDebug($"Starting the heartbeat timer with an immediate start and interval of {ConstantsConfig.HeartbeatInterval} ms.");
            _heartbeatTimer.Change(0, ConstantsConfig.HeartbeatInterval);
            _logger.LogDebug("Heartbeat timer has been successfully started.");
        }

        public void Start()
        {
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

            // 开始接受普通套接字客户端连接，属于关键操作步骤，使用Debug记录
            _logger.LogDebug($"Starting to accept socket clients on port {_port}.");
            AcceptSocketClients();
            _logger.LogDebug("Accepting socket clients process has been initiated.");

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
        }
        public void Stop()
        {
            // 停止心跳定时器，属于系统定时任务的操作，使用Debug记录
            _logger.LogDebug("Halting the heartbeat timer by setting its interval to infinite.");
            _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.LogDebug("The heartbeat timer has been successfully stopped.");

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

            if (_sslListener != null)
            {
                _sslListener.Stop();
                _logger.LogDebug("_sslListener has been stopped and disposed.");
            }
            else
            {
                _logger.LogTrace("The _sslListener listener is null, no disposal operation is required.");
            }
        }
        private async void AcceptSslClients()
        {
            _logger.LogTrace("Enter AcceptSslClients loop");

            while (_isRunning)
            {
                try
                {
                    // 1. 接受SSL客户端连接
                    var sslClient = await _sslListener.AcceptTcpClientAsync();
                    _logger.LogDebug($"Accepted new SSL client: {sslClient.Client.RemoteEndPoint}");

                    // 分配客户端ID并
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _ClientConnectionManager.CreateClient(clientId).ConnectAsync();

                    // 2. 创建SSL流
                    var sslStream = new SslStream(sslClient.GetStream(), false, _SSLManager.ValidateClientCertificate);
                    _logger.LogTrace($"Created SslStream for client: {sslClient.Client.RemoteEndPoint}");

                    // 3. 配置SSL验证参数
                    var sslOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCert,                  // 服务器证书
                        ClientCertificateRequired = true,                 // 强制客户端证书验证
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, // 支持的TLS版本
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck // 生产环境建议启用CRL检查
                    };
                    _logger.LogDebug("Configured SSL authentication options");

                    try
                    {
                        // 4. 执行SSL握手
                        await sslStream.AuthenticateAsServerAsync(sslOptions);
                        _logger.LogInformation($"SSL handshake completed for client: {sslClient.Client.RemoteEndPoint}");

                        // 5. 创建配置对象
                        var client = new ClientConfig(clientId, sslStream);
                        _clients.TryAdd(clientId, client);

                        _logger.LogInformation($"SSL Client {clientId} connected: {sslClient.Client.RemoteEndPoint}");
                        _logger.LogTrace($"Added client {clientId} to _clients (count={_clients.Count})");

                        _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();

                        Interlocked.Increment(ref _connectSSL);

                        // 6. 启动客户端消息处理任务
                        _ = HandleClient(client);
                        _logger.LogDebug($"Started HandleClient task for SSL client {clientId}");

                    }
                    catch (AuthenticationException authEx)
                    {
                        _logger.LogCritical($"SSL authentication failed: {authEx.Message}");
                        if (authEx.InnerException != null)
                        {
                            _logger.LogCritical($"Inner exception: {authEx.InnerException.Message}");
                        }

                        _ClientConnectionManager.TryGetClientById(clientId)?.ErrorAsync();
                    }
                    catch (Exception ex)
                    {
                        _ClientConnectionManager.TryGetClientById(clientId)?.ErrorAsync();
                        _logger.LogCritical($"Error accepting client: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"Error accepting client: {ex.Message}");
                    _logger.LogWarning($"Retrying in 100ms...");
                    await Task.Delay(100);
                }

                _logger.LogTrace("Exited AcceptSslClients loop (server stopped)");
            }
        }

        private async void AcceptSocketClients()
        {
            _logger.LogTrace("Enter AcceptSocketClients loop");

            while (_isRunning)
            {
                try
                {
                    // 1. 接受Socket客户端连接
                    var clientSocket = await _listener.AcceptAsync();
                    _logger.LogDebug($"Accepted new socket client: {clientSocket.RemoteEndPoint}");

                    // 分配客户端ID并
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _ClientConnectionManager.CreateClient(clientId).ConnectAsync();

                    // 2. 创建配置对象
                    var client = new ClientConfig(clientId, clientSocket);
                    _clients.TryAdd(clientId, client);

                    _logger.LogInformation($"Socket Client {clientId} connected: {clientSocket.RemoteEndPoint}");
                    _logger.LogTrace($"Added client {clientId} to _clients (count={_clients.Count})");

                    _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();

                    Interlocked.Increment(ref _connectSocket);

                    // 3. 启动客户端消息处理任务
                    _ = HandleClient(client);
                    _logger.LogDebug($"Started HandleClient task for socket client {clientId}");
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // 正常关闭时忽略（如服务器Stop调用）
                    _logger.LogTrace("Socket accept interrupted (expected shutdown)");
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"Socket Accept error: {ex.Message}");
                    _logger.LogWarning($"Retrying socket accept in 100ms...");
                    await Task.Delay(100);
                }
            }

            _logger.LogTrace("Exited AcceptSocketClients loop (server stopped)");
        }
        /// <summary>
        /// 定期检查客户端心跳状态（超时断开无效连接）
        /// </summary>
        private void CheckHeartbeats()
        {
            var now = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(ConstantsConfig.TimeoutSeconds);
            _logger.LogTrace($"Heartbeat check started (Timeout={ConstantsConfig.TimeoutSeconds}s)");

            // 遍历所有客户端连接
            foreach (var client in _clients.ToList()) // 复制列表避免迭代时修改集合
            {
                var clientId = client.Key;
                var clientConfig = client.Value;

                // 计算客户端最后活动时间差
                var elapsed = now - clientConfig.LastActivity;
                _logger.LogDebug($"Client {clientId} last activity: {clientConfig.LastActivity} ({elapsed.TotalSeconds:F1}s ago)");

                if (elapsed > timeout)
                {
                    // 心跳超时，断开客户端连接
                    _logger.LogWarning($"Client {clientId} heartbeat timeout ({elapsed.TotalSeconds:F1}s > {ConstantsConfig.TimeoutSeconds}s)");
                    DisconnectClient(clientId);

                    // 从客户端列表移除
                    if (_clients.TryRemove(clientId, out _))
                    {
                        _logger.LogInformation($"Client {clientId} removed due to heartbeat timeout");
                    }
                    else
                    {
                        _logger.LogError($"Failed to remove client {clientId} after timeout");
                    }
                }
            }

            _logger.LogTrace("Heartbeat check completed");
        }

        /// <summary>
        /// 处理客户端连接（生产者逻辑：接收消息并按优先级入队）
        /// </summary>
        /// <param name="client">客户端配置对象</param>
        private async Task HandleClient(ClientConfig client)
        {
            _logger.LogTrace($"Client {client.Id} connection started (RemoteEndPoint={client.Socket?.RemoteEndPoint})");

            try
            {
                // 初始化数据流（普通Socket或SSL流）
                Stream stream = client.Socket != null
                    ? new NetworkStream(client.Socket)
                    : client.SslStream;
                _logger.LogDebug($"Client {client.Id} using {stream.GetType().Name} for communication");

                while (_isRunning && _isReceiving)
                {
                    try
                    {
                        // 1. 接收消息头部（固定8字节）
                        byte[] headerBuffer = new byte[8];
                        _logger.LogTrace($"Client {client.Id} reading header (8 bytes)");
                        if (!await ReadFullAsync(stream, headerBuffer, 8))
                        {
                            _logger.LogWarning($"Client {client.Id} disconnected while reading header");
                            return;
                        }
                        _logger.LogDebug($"Client {client.Id} header received successfully");

                        // 2. 解析协议头部
                        _logger.LogTrace($"Client {client.Id} parsing header bytes");
                        if (!ProtocolHeaderExtensions.TryFromBytes(headerBuffer, out var header))
                        {
                            _logger.LogWarning($"Client {client.Id} invalid header format: {BitConverter.ToString(headerBuffer)}");
                            continue;
                        }
                        _logger.LogDebug($"Client {client.Id} header parsed: Version={header.Version}, Length={header.MessageLength}");

                        // 3. 校验协议版本
                        if (!ConstantsConfig.config.SupportedVersions.Contains((byte)header.Version))
                        {
                            _logger.LogWarning($"Client {client.Id} unsupported version {header.Version} (supported: {string.Join(",", ConstantsConfig.config.SupportedVersions)})");
                            continue;
                        }
                        _logger.LogDebug($"Client {client.Id} protocol version {header.Version} verified");

                        // 4. 接收消息体
                        byte[] payloadBuffer = new byte[header.MessageLength];
                        _logger.LogTrace($"Client {client.Id} reading payload ({header.MessageLength} bytes)");
                        if (!await ReadFullAsync(stream, payloadBuffer, (int)header.MessageLength))
                        {
                            _logger.LogWarning($"Client {client.Id} disconnected while reading payload");
                            return;
                        }
                        _logger.LogDebug($"Client {client.Id} payload received ({header.MessageLength} bytes)");

                        // 5. 组装完整数据包
                        byte[] fullPacket = new byte[8 + header.MessageLength];
                        Buffer.BlockCopy(headerBuffer, 0, fullPacket, 0, 8);
                        Buffer.BlockCopy(payloadBuffer, 0, fullPacket, 8, (int)header.MessageLength);
                        _logger.LogTrace($"Client {client.Id} assembled full packet (size={fullPacket.Length} bytes)");

                        // 6. 解析数据包
                        _logger.LogTrace($"Client {client.Id} trying to parse packet");
                        var (success, packet, error) = ProtocolPacketWrapper.TryFromBytes(fullPacket);
                        if (!success)
                        {
                            _logger.LogWarning($"Client {client.Id} packet parsing failed: {error}");
                            continue;
                        }
                        _logger.LogDebug($"Client {client.Id} packet parsed successfully (Priority={packet.Data.Priority})");

                        // 更新客户端活动时间
                        client.UpdateActivity();
                        client.SetValue(packet.Data.Sourceid);
                        _logger.LogTrace($"Client {client.Id} activity time updated");

                        // 判断是否为视频或语音通信请求
                        if (IsVideoOrVoiceRequest(packet.Data))
                        {
                            // 查找目标客户端
                            var targetClient = _clients.Values.FirstOrDefault(c => c.UniqueId == packet.Data.Targetid);
                            if (targetClient != null)
                            {
                                // 添加监测功能
                                if (!_isRealTimeTransferAllowed)
                                {
                                    _logger.LogDebug("Data RealTime transfer is paused");
                                    await _outMessageManager.SendInfoDate(targetClient, packet.Data);
                                    continue;
                                }
                                else
                                {
                                    // 建立直接连接
                                    await EstablishDirectConnection(client, targetClient);
                                }
                                continue;
                            }
                            else
                            {
                                _logger.LogWarning($"Client {client.Id} target client {packet.Data.Targetid} not found");
                                continue;
                            }
                        }

                        // 创建消息对象
                        var message = new ClientMessage
                        {
                            Client = client,
                            Data = packet.Data,
                            ReceivedTime = DateTime.Now
                        };

                        if (ConstantsConfig.IsUnityServer)
                        {

                            // 背压策略：丢弃低优先级消息（队列积压时）
                            bool isQueueFull = _InmessageManager._messageHighQueue.Reader.Count > ConstantsConfig.MaxQueueSize ||
                                               _InmessageManager._messageMediumQueue.Reader.Count > ConstantsConfig.MaxQueueSize ||
                                               _InmessageManager._messagelowQueue.Reader.Count > ConstantsConfig.MaxQueueSize;

                            if (isQueueFull && message.Data.Priority == DataPriority.Low)
                            {
                                _logger.LogCritical($"Client {client.Id} discarded low-priority message (queue full: High={_InmessageManager._messageHighQueue.Reader.Count}, Medium={_InmessageManager._messageMediumQueue.Reader.Count}, Low={_InmessageManager._messagelowQueue.Reader.Count})");
                                continue;
                            }

                            // 按优先级入队
                            switch (packet.Data.Priority)
                            {
                                case DataPriority.Low:
                                    await _InmessageManager._messagelowQueue.Writer.WriteAsync(message);
                                    _logger.LogDebug($"Client {client.Id} low-priority message enqueued (Id={message.Client.Id})");
                                    break;
                                case DataPriority.High:
                                    await _InmessageManager._messageHighQueue.Writer.WriteAsync(message);
                                    _logger.LogDebug($"Client {client.Id} high-priority message enqueued (Id={message.Client.Id})");
                                    break;
                                case DataPriority.Medium:
                                    await _InmessageManager._messageMediumQueue.Writer.WriteAsync(message);
                                    _logger.LogDebug($"Client {client.Id} medium-priority message enqueued (Id={message.Client.Id})");
                                    break;
                            }

                            // 队列积压监控与背压（按优先级分级处理）
                            await MonitorQueueBackpressure(client, packet.Data.Priority, (int)header.MessageLength);
                        }
                        else
                        {
                            _InmessageManager.ProcessMessageWithPriority(message, message.Data.Priority);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical($"Client {client.Id} unexpected error: {ex.Message}  ");
                        break; // 终止当前客户端处理循环
                    }
                }
            }
            finally
            {
                DisconnectClient(client.Id);
                _logger.LogInformation($"Client {client.Id} connection terminated");
            }
        }

        /// <summary>
        /// 监控队列积压并执行背压策略
        /// </summary>
        private async Task MonitorQueueBackpressure(ClientConfig client, DataPriority priority, int messageSize)
        {
            switch (priority)
            {
                case DataPriority.Low:
                    if (_InmessageManager._messagelowQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} LOW QUEUE BACKPRESSURE: {_InmessageManager._messagelowQueue.Reader.Count} messages积压");
                        await ImplementBackpressure(client, TimeSpan.FromSeconds(1)); // 低优先级暂停1秒
                    }
                    break;
                case DataPriority.Medium:
                    if (_InmessageManager._messageMediumQueue.Reader.Count > ConstantsConfig.MaxQueueSize * 0.9) // 90%阈值预警
                    {
                        _logger.LogWarning($"Client {client.Id} MEDIUM QUEUE NEAR BACKPRESSURE: {_InmessageManager._messageMediumQueue.Reader.Count}/{ConstantsConfig.MaxQueueSize}");
                    }
                    if (_InmessageManager._messageMediumQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} MEDIUM QUEUE BACKPRESSURE: {_InmessageManager._messageMediumQueue.Reader.Count} messages积压");
                        await ImplementBackpressure(client, TimeSpan.FromMilliseconds(600)); // 中等优先级暂停600ms
                    }
                    break;
                case DataPriority.High:
                    if (_InmessageManager._messageHighQueue.Reader.Count > ConstantsConfig.MaxQueueSize * 0.9) // 90%阈值预警
                    {
                        _logger.LogWarning($"Client {client.Id} HIGH QUEUE NEAR BACKPRESSURE: {_InmessageManager._messageHighQueue.Reader.Count}/{ConstantsConfig.MaxQueueSize}");
                    }
                    if (_InmessageManager._messageHighQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} HIGH QUEUE BACKPRESSURE: {_InmessageManager._messageHighQueue.Reader.Count} messages积压");
                        await ImplementBackpressure(client, TimeSpan.FromMilliseconds(200)); // 高优先级暂停200ms
                    }
                    break;
            }
        }

        /// <summary>
        /// 执行背压策略（暂停接收新消息）
        /// </summary>
        private async Task ImplementBackpressure(ClientConfig client, TimeSpan delay)
        {
            _logger.LogCritical($"Client {client.Id} applying backpressure: pausing receive for {delay.TotalMilliseconds}ms");
            _isReceiving = false; // 暂停接收新消息
            await Task.Delay(delay);
            _isReceiving = true;
            _logger.LogCritical($"Client {client.Id} backpressure released: resume receiving");
        }

        // 判断是否为视频或语音通信请求
        private bool IsVideoOrVoiceRequest(CommunicationData data)
        {
            // 这里需要根据实际的协议定义来判断
            // 假设存在一个字段来标识视频或语音通信请求
            return data.InfoType == InfoType.CtcVideo || data.InfoType == InfoType.CtcVoice;
        }

        // 建立直接连接
        private async Task EstablishDirectConnection(ClientConfig client1, ClientConfig client2)
        {
            _logger.LogTrace($"Starting to establish direct connection between client {client1.UniqueId} and client {client2.UniqueId}.");

            try
            {
                _logger.LogDebug($"Opening network streams for client {client1.UniqueId} and client {client2.UniqueId}.");
                using var stream1 = new NetworkStream(client1.Socket);
                using var stream2 = new NetworkStream(client2.Socket);

                _logger.LogInformation($"Successfully opened network streams for client {client1.UniqueId} and client {client2.UniqueId}. Establishing bidirectional data transfer.");

                var task1 = CopyStreamAsync(stream1, stream2);
                var task2 = CopyStreamAsync(stream2, stream1);

                await Task.WhenAll(task1, task2);

                _logger.LogInformation($"Direct connection between client {client1.UniqueId} and client {client2.UniqueId} has been successfully established and data transfer completed.");
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning($"One of the sockets for client {client1.UniqueId} or client {client2.UniqueId} is disposed. Error: {ex.Message}");
            }
            catch (IOException ex)
            {
                _logger.LogError($"An I/O error occurred while establishing direct connection between client {client1.UniqueId} and client {client2.UniqueId}. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Unexpected error while establishing direct connection between client {client1.UniqueId} and client {client2.UniqueId}. Error: {ex.Message}");
            }
            finally
            {
                _logger.LogTrace($"Ending the process of establishing direct connection between client {client1.UniqueId} and client {client2.UniqueId}.");
            }
        }

        // 异步复制流
        private async Task CopyStreamAsync(Stream source, Stream destination)
        {
            _logger.LogTrace($"Starting to copy data from source stream to destination stream.");
            try
            {
                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    _logger.LogDebug($"Read {bytesRead} bytes from source stream. Writing to destination stream.");
                    await destination.WriteAsync(buffer, 0, bytesRead);
                    _logger.LogDebug($"Successfully wrote {bytesRead} bytes to destination stream.");
                }
                _logger.LogInformation($"Data copying from source stream to destination stream completed.");
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning($"One of the streams is disposed during data copying. Error: {ex.Message}");
            }
            catch (IOException ex)
            {
                _logger.LogError($"An I/O error occurred during data copying. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Unexpected error during data copying. Error: {ex.Message}");
            }
            finally
            {
                _logger.LogTrace($"Ending the data copying process from source stream to destination stream.");
            }
        }

        public void ModifyRealTimeTransfer(bool value)
        {
            _isRealTimeTransferAllowed = value;
        }

        /// <summary>
        /// 异步读取指定数量的字节到缓冲区中，确保读取的字节数达到指定的数量。
        /// </summary>
        /// <param name="stream">要读取数据的流。</param>
        /// <param name="buffer">用于存储读取数据的缓冲区。</param>
        /// <param name="count">需要读取的字节数。</param>
        /// <returns>如果成功读取指定数量的字节，返回 true；否则返回 false。</returns>
        private async Task<bool> ReadFullAsync(Stream stream, byte[] buffer, int count)
        {
            // 初始化偏移量，用于记录已经读取的字节数
            int offset = 0;
            _logger.LogTrace($"Starting to read {count} bytes from the stream.");

            // 循环读取数据，直到读取的字节数达到指定数量
            while (offset < count)
            {
                // 异步读取数据到缓冲区中
                int read = await stream.ReadAsync(buffer, offset, count - offset);
                _logger.LogTrace($"Read {read} bytes from the stream at offset {offset}.");

                // 如果读取的字节数为 0，说明连接已经关闭
                if (read == 0)
                {
                    _logger.LogWarning($"Connection closed while reading. Expected: {count} bytes, Read: {offset} bytes.");
                    return false;
                }

                // 更新偏移量
                offset += read;
                _logger.LogTrace($"Read progress: {offset}/{count} bytes.");
            }

            _logger.LogTrace($"Successfully read {count} bytes from the stream.");
            return true;
        }

        private async void DisconnectClient(uint clientId)
        {
            _logger.LogTrace($"Disconnecting client {clientId}...");

            if (_clients.TryRemove(clientId, out var client))
            {
                try
                {
                    await _ClientConnectionManager.TryGetClientById(clientId)?.DisconnectAsync();
                    // 1. 关闭网络连接
                    if (client.Socket != null)
                    {
                        Interlocked.Decrement(ref _connectSocket);
                        client.Socket.Shutdown(SocketShutdown.Both);
                        _logger.LogDebug($"Shut down socket for client {clientId}");
                    }
                    else if (client.SslStream != null)
                    {
                        Interlocked.Decrement(ref _connectSSL);
                        client.SslStream.Close();
                        _logger.LogDebug($"Closed SSL stream for client {clientId}");
                        client.Socket?.Dispose();
                    }

                    client.IsConnect = false;
                    _logger.LogTrace($"Marked client {clientId} as disconnected");

                    await _ClientConnectionManager.TryGetClientById(clientId)?.DisconnectCompleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error closing client {clientId} connection: {ex.Message}");

                    await _ClientConnectionManager.TryGetClientById(clientId)?.ErrorAsync();
                }
                finally
                {
                    client.Socket?.Dispose();
                    _logger.LogDebug($"Disposed socket for client {clientId}");
                }

                // 2. 统计流量数据
                var totalRec = client.BytesReceived + client.FileBytesReceived;
                var totalSent = client.BytesSent + client.FileBytesSent;
                var totalCountRec = client.ReceiveCount + client.ReceiveFileCount;
                var totalCountSent = client.SendCount + client.SendFileCount;

                // 3. 记录断开日志（包含详细流量统计）
                string message = $"Client {clientId} disconnected. " +
                                $"Normal: Recv {Function.FormatBytes(client.BytesReceived)} Send {Function.FormatBytes(client.BytesSent)} | " +
                                $"File: Recv {Function.FormatBytes(client.FileBytesReceived)} Send {Function.FormatBytes(client.FileBytesSent)} | " +
                                $"Total: Recv {Function.FormatBytes(totalRec)} Send {Function.FormatBytes(totalSent)} |" +
                                $"Count Normal: Recv {client.ReceiveCount} Send {client.SendCount} | " +
                                $"Count File: Recv {client.ReceiveFileCount} Send {client.SendFileCount} | " +
                                $"Count Total: Recv {totalCountRec} Send {totalCountSent} |";

                _logger.LogWarning(message); // 警告级别日志（连接断开属于异常但非错误）
                _logger.LogTrace($"Client {clientId} removed from _clients (current count={_clients.Count})");

                if (_historyclients.TryAdd(clientId, client))
                {
                    _logger.LogDebug($"Client {clientId} added from _clients (current count={_historyclients.Count})");
                }
            }
            else
            {
                _logger.LogTrace($"Client {clientId} not found in _clients (already disconnected)");
            }
        }
    }
}
