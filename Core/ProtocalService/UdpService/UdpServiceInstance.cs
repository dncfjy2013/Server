using Server.Core.Common;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.ProtocalService.UdpService
{
    /// <summary>
    /// UDP服务实例，支持多加密方案的网络服务实现
    /// 负责处理UDP客户端连接、数据加密/解密及通信管理
    /// </summary>
    public sealed class UdpServiceInstance
    {
        private readonly ILogger _logger;                  // 日志记录器，用于记录服务运行状态和异常
        private readonly int _udpPort;                     // UDP服务监听的端口号
        private readonly ConcurrentDictionary<uint, ClientEncryptionContext> _clientContexts = new ConcurrentDictionary<uint, ClientEncryptionContext>();
        // 存储客户端上下文的并发字典，键为客户端ID
        private readonly SemaphoreSlim _connectionSemaphore;
        // 连接信号量，限制最大并发连接数
        private readonly Dictionary<EncryptionProtocol, Func<byte[], IEncryptionService>> _encryptionFactories;
        // 加密协议工厂字典，根据协议类型创建加密服务实例
        private readonly byte[] _masterKey;                // 主加密密钥，用于生成客户端加密密钥
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        // 用于取消异步操作的令牌源
        private readonly Timer _cleanupTimer;              // 清理过期客户端上下文的定时器
        private readonly int _maxConnections;              // 最大允许的客户端连接数

        private readonly ConnectionManager _connectionManager;

        private UdpClient? _udpListener;                   // UDP监听客户端
        private uint _nextClientId;                        // 下一个可用的客户端ID
        private bool _isDisposed;                          // 标识服务是否已释放资源

        /// <summary>
        /// 初始化UDP服务实例
        /// </summary>
        /// <param name="logger">日志记录器</param>
        /// <param name="udpPort">UDP监听端口</param>
        /// <param name="nextClientId">客户端ID生成起始值(引用传递)</param>
        /// <param name="masterKey">可选的主加密密钥，若未提供则自动生成</param>
        public UdpServiceInstance(ILogger logger, int udpPort, ref uint nextClientId, ConnectionManager connectionManager, byte[]? masterKey = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _udpPort = udpPort;
            _connectionManager = connectionManager;
            _nextClientId = nextClientId;
            _maxConnections = 100; // 设置最大连接数为100
            _masterKey = masterKey ?? GenerateSecureMasterKey(); // 生成或使用提供的主密钥

            // 初始化信号量，限制并发连接数
            _connectionSemaphore = new SemaphoreSlim(_maxConnections, _maxConnections);

            // 初始化清理定时器，每分钟检查一次过期客户端
            _cleanupTimer = new Timer(CleanupExpiredContexts, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            _logger.LogInformation($"Using master key: {BitConverter.ToString(_masterKey).Replace("-", "")}");

            // 注册加密协议工厂，将协议类型映射到对应的加密服务创建函数
            _encryptionFactories = new Dictionary<EncryptionProtocol, Func<byte[], IEncryptionService>>
        {
            { EncryptionProtocol.AesGcm, key => new AesGcmEncryptionService(key) },
            { EncryptionProtocol.AesCbcHmac, key => new AesCbcHmacEncryptionService(key) }
        };
        }

        /// <summary>
        /// 启动UDP服务监听
        /// </summary>
        public void Start()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(UdpServiceInstance));

            try
            {
                _logger.LogDebug($"Starting UDP listener on port {_udpPort}");
                // 初始化UDP监听客户端并配置属性
                _udpListener = new UdpClient(_udpPort)
                {
                    Client =
                {
                    ReceiveTimeout = 10000,    // 接收超时10秒
                    SendTimeout = 10000,       // 发送超时10秒
                    DontFragment = true,       // 禁止数据包分片
                    ExclusiveAddressUse = false // 允许多个套接字共享同一端口
                }
                };

                _logger.LogInformation($"UDP listener started on port {_udpPort}");
                // 启动异步接受客户端连接的任务
                _ = AcceptUdpClientsAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Failed to start UDP service: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 停止UDP服务并释放资源
        /// </summary>
        public void Stop()
        {
            if (_isDisposed)
                return;

            try
            {
                // 取消所有异步操作
                _cancellationTokenSource.Cancel();
                // 释放定时器资源
                _cleanupTimer.Dispose();
                // 释放UDP监听客户端资源
                _udpListener?.Dispose();
                // 释放信号量资源
                _connectionSemaphore.Dispose();

                var contextCount = _clientContexts.Count;
                _clientContexts.Clear();
                _logger.LogInformation($"Stopped UDP service. Cleared {contextCount} client contexts.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error stopping UDP service: {ex.Message}");
            }
        }

        /// <summary>
        /// 异步接受UDP客户端连接的主循环
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task AcceptUdpClientsAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("UDP client acceptance loop started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 等待信号量，限制并发连接数
                    await _connectionSemaphore.WaitAsync(cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // 异步接收客户端数据
                    var receiveResult = await _udpListener!.ReceiveAsync()
                        .ConfigureAwait(false);

                    // 分配新的客户端ID
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _logger.LogDebug($"New client connected: {receiveResult.RemoteEndPoint}, ClientId: {clientId}");

                    // 创建并存储客户端上下文
                    var context = new ClientEncryptionContext
                    {
                        ClientId = clientId,
                        RemoteEndPoint = receiveResult.RemoteEndPoint,
                        ConnectionTime = DateTime.UtcNow
                    };

                    _clientContexts[clientId] = context;

                    // 处理客户端数据的异步任务，并在完成后释放信号量
                    _ = HandleUdpDataAsync(clientId, receiveResult.RemoteEndPoint, receiveResult.Buffer, cancellationToken)
                        .ContinueWith(t => _connectionSemaphore.Release(), TaskContinuationOptions.OnlyOnRanToCompletion);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogTrace("UDP client acceptance loop cancelled");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        _logger.LogWarning("UDP listener disposed while running");
                    break;
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
                {
                    _logger.LogDebug("Client connection reset by peer");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error accepting UDP client: {ex.Message}", ex);

                    // 发生异常时释放信号量
                    if (_connectionSemaphore.CurrentCount < _maxConnections)
                        _connectionSemaphore.Release();

                    // 短暂延迟后重试
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
            }

            _logger.LogTrace("UDP client acceptance loop stopped");
        }

        /// <summary>
        /// 处理客户端发送的UDP数据
        /// </summary>
        /// <param name="clientId">客户端ID</param>
        /// <param name="remoteEndPoint">客户端端点</param>
        /// <param name="data">接收到的数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        private async Task HandleUdpDataAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (data.Length < 1)
            {
                _logger.LogWarning($"Invalid data from client {clientId}: empty or null");
                return;
            }

            try
            {
                // 解析数据中的加密协议类型
                var protocol = (EncryptionProtocol)data[0];
                _logger.LogDebug($"Client {clientId} using protocol: {protocol}");

                // 获取或创建客户端上下文
                var context = _clientContexts.GetOrAdd(clientId, id => new ClientEncryptionContext
                {
                    ClientId = id,
                    RemoteEndPoint = remoteEndPoint,
                    ConnectionTime = DateTime.UtcNow
                });

                // 更新上下文信息
                context.LastUsedProtocol = protocol;
                context.LastActivityTime = DateTime.UtcNow;

                // 根据协议类型处理数据
                switch (protocol)
                {
                    case EncryptionProtocol.None:
                        await HandlePlaintextDataAsync(clientId, remoteEndPoint, data, cancellationToken);
                        break;
                    case EncryptionProtocol.AesGcm:
                    case EncryptionProtocol.AesCbcHmac:
                        await HandleEncryptedDataAsync(clientId, remoteEndPoint, data, cancellationToken);
                        break;
                    default:
                        _logger.LogWarning($"Unsupported encryption protocol: {protocol} from client {clientId}");
                        await SendProtocolErrorAsync(clientId, remoteEndPoint, "Unsupported protocol", cancellationToken);
                        break;
                }
            }
            catch (CryptographicException cex)
            {
                _logger.LogWarning($"Cryptographic error from client {clientId}: {cex.Message}");
                await SendProtocolErrorAsync(clientId, remoteEndPoint, "Encryption error", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling data from client {clientId}: {ex.Message}", ex);
                await SendGeneralErrorAsync(clientId, remoteEndPoint, ex.Message, cancellationToken);
            }
        }

        /// <summary>
        /// 处理明文数据（未加密）
        /// </summary>
        private async Task HandlePlaintextDataAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (data.Length < 2)
            {
                _logger.LogWarning($"Invalid plaintext data from client {clientId}");
                return;
            }

            try
            {
                // 解析明文消息
                var message = Encoding.UTF8.GetString(data, 1, data.Length - 1);
                _logger.LogInformation($"Received plaintext from {remoteEndPoint}: {message}");

                // 构造响应消息
                var response = Encoding.UTF8.GetBytes($"Server received plaintext: {message}");
                // 使用AES-GCM协议加密并发送响应
                await SendEncryptedResponseAsync(clientId, remoteEndPoint, response, EncryptionProtocol.AesGcm, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling plaintext: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 处理加密数据
        /// </summary>
        private async Task HandleEncryptedDataAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (!_clientContexts.TryGetValue(clientId, out var context))
            {
                _logger.LogWarning($"No context for client {clientId}, dropping packet");
                return;
            }

            var protocol = context.LastUsedProtocol;
            if (!_encryptionFactories.TryGetValue(protocol, out var factory))
            {
                _logger.LogWarning($"Unsupported protocol {protocol} for client {clientId}");
                await SendProtocolErrorAsync(clientId, remoteEndPoint, "Unsupported protocol version", cancellationToken);
                return;
            }

            try
            {
                // 提取加密数据部分（跳过协议头）
                var encryptedData = new byte[data.Length - 1];
                Array.Copy(data, 1, encryptedData, 0, encryptedData.Length);

                // 创建加密服务实例
                var encryptionService = factory(_masterKey);
                // 解密数据
                var decryptedData = await encryptionService.DecryptAsync(encryptedData).ConfigureAwait(false);
                var message = Encoding.UTF8.GetString(decryptedData);

                _logger.LogInformation($"Decrypted message from {remoteEndPoint} (Protocol {protocol}): {message}");

                // 构造响应并加密发送
                var response = Encoding.UTF8.GetBytes($"Server received: {message} (Protocol {protocol})");
                await SendEncryptedResponseAsync(clientId, remoteEndPoint, response, protocol, cancellationToken);
            }
            catch (CryptographicException cex)
            {
                _logger.LogWarning($"Decryption failed for client {clientId}: {cex.Message}");
                await SendProtocolErrorAsync(clientId, remoteEndPoint, "Decryption failed", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling encrypted data: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 发送加密响应给客户端
        /// </summary>
        private async Task SendEncryptedResponseAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, EncryptionProtocol protocol, CancellationToken cancellationToken)
        {
            if (!_encryptionFactories.TryGetValue(protocol, out var factory))
            {
                _logger.LogWarning($"Cannot send response: unsupported protocol {protocol}");
                return;
            }

            try
            {
                // 创建加密服务实例
                var encryptionService = factory(_masterKey);
                // 加密响应数据
                var encryptedData = await encryptionService.EncryptAsync(data).ConfigureAwait(false);

                // 从数组池中获取缓冲区，避免频繁分配内存
                var fullPacket = ArrayPool<byte>.Shared.Rent(1 + encryptedData.Length);
                try
                {
                    // 构造完整数据包（协议头+加密数据）
                    fullPacket[0] = (byte)protocol;
                    Array.Copy(encryptedData, 0, fullPacket, 1, encryptedData.Length);

                    // 发送数据包给客户端
                    await _udpListener!.SendAsync(fullPacket, 1 + encryptedData.Length, remoteEndPoint)
                        .ConfigureAwait(false);

                    _logger.LogDebug($"Sent encrypted response to {remoteEndPoint}, Protocol: {protocol}");
                }
                finally
                {
                    // 将缓冲区归还数组池，以便重用
                    ArrayPool<byte>.Shared.Return(fullPacket);
                }
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
            {
                _logger.LogWarning($"Client disconnected while sending response: {remoteEndPoint}");
                RemoveClientContext(clientId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error sending encrypted response: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 发送协议错误响应给客户端
        /// </summary>
        private async Task SendProtocolErrorAsync(uint clientId, IPEndPoint remoteEndPoint, string message, CancellationToken cancellationToken)
        {
            try
            {
                // 构造错误消息
                var errorData = Encoding.UTF8.GetBytes($"[PROTOCOL_ERROR] {message}");
                var packet = new byte[1 + errorData.Length];
                packet[0] = (byte)EncryptionProtocol.None; // 使用无加密协议发送错误消息
                Array.Copy(errorData, 0, packet, 1, errorData.Length);

                // 发送错误响应
                await _udpListener!.SendAsync(packet, packet.Length, remoteEndPoint)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to send protocol error: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送常规错误响应给客户端
        /// </summary>
        private async Task SendGeneralErrorAsync(uint clientId, IPEndPoint remoteEndPoint, string message, CancellationToken cancellationToken)
        {
            try
            {
                // 构造错误消息
                var errorData = Encoding.UTF8.GetBytes($"[ERROR] {message}");
                var packet = new byte[1 + errorData.Length];
                packet[0] = (byte)EncryptionProtocol.None; // 使用无加密协议发送错误消息
                Array.Copy(errorData, 0, packet, 1, errorData.Length);

                // 发送错误响应
                await _udpListener!.SendAsync(packet, packet.Length, remoteEndPoint)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to send general error: {ex.Message}");
            }
        }

        /// <summary>
        /// 移除客户端上下文
        /// </summary>
        private void RemoveClientContext(uint clientId)
        {
            if (_clientContexts.TryRemove(clientId, out _))
                _logger.LogDebug($"Removed client context: {clientId}");
        }

        /// <summary>
        /// 定时清理过期的客户端上下文
        /// </summary>
        private void CleanupExpiredContexts(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                // 查找5分钟内无活动的客户端上下文
                var expiredIds = _clientContexts
                    .Where(kv => (now - kv.Value.LastActivityTime).TotalMinutes > 5)
                    .Select(kv => kv.Key)
                    .ToList();

                // 移除过期的客户端上下文
                foreach (var clientId in expiredIds)
                {
                    RemoveClientContext(clientId);
                }

                if (expiredIds.Count > 0)
                    _logger.LogDebug($"Cleaned up {expiredIds.Count} expired client contexts");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during cleanup: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 生成安全的主加密密钥
        /// </summary>
        private byte[] GenerateSecureMasterKey()
        {
            using var rng = RandomNumberGenerator.Create();
            var key = new byte[32]; // 32字节(256位)密钥
            rng.GetBytes(key);
            return key;
        }
    }

    /// <summary>
    /// 客户端加密上下文，存储客户端连接状态和信息
    /// </summary>
    public class ClientEncryptionContext
    {
        public uint ClientId { get; set; }                 // 客户端唯一标识
        public IPEndPoint RemoteEndPoint { get; set; }     // 客户端网络端点
        public EncryptionProtocol LastUsedProtocol { get; set; } // 最后使用的加密协议
        public DateTime ConnectionTime { get; set; }       // 连接建立时间
        public DateTime LastActivityTime { get; set; } = DateTime.UtcNow; // 最后活动时间
    }
}