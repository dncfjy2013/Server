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
    /// UDP服务实例，支持多加密方案
    /// </summary>
    public sealed class UdpServiceInstance
    {
        private readonly ILogger _logger;
        private readonly int _udpPort;
        private readonly ConcurrentDictionary<uint, ClientEncryptionContext> _clientContexts = new();
        private readonly SemaphoreSlim _connectionSemaphore;
        private readonly Dictionary<EncryptionProtocol, Func<byte[], IEncryptionService>> _encryptionFactories;
        private readonly byte[] _masterKey;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Timer _cleanupTimer;
        private readonly int _maxConnections;
        private readonly ConnectionManager _connectionManager;

        private UdpClient? _udpListener;
        private uint _nextClientId;
        private bool _isDisposed;

        public UdpServiceInstance(ILogger logger, int udpPort, ref uint nextClientId, ConnectionManager connectionManager, byte[]? masterKey = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionManager = connectionManager;
            _udpPort = udpPort;
            _nextClientId = nextClientId;
            _maxConnections = 100;
            _masterKey = masterKey ?? GenerateSecureMasterKey();
            _connectionSemaphore = new SemaphoreSlim(_maxConnections, _maxConnections);
            _cleanupTimer = new Timer(CleanupExpiredContexts, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            _logger.LogInformation($"Using master key: {BitConverter.ToString(_masterKey).Replace("-", "")}");

            _encryptionFactories = new Dictionary<EncryptionProtocol, Func<byte[], IEncryptionService>>
        {
            { EncryptionProtocol.AesGcm, key => new AesGcmEncryptionService(key) },
            { EncryptionProtocol.AesCbcHmac, key => new AesCbcHmacEncryptionService(key) }
        };
        }

        public void Start()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(UdpServiceInstance));

            try
            {
                _logger.LogDebug($"Starting UDP listener on port {_udpPort}");
                _udpListener = new UdpClient(_udpPort)
                {
                    Client =
                {
                    ReceiveTimeout = 10000,
                    SendTimeout = 10000,
                    DontFragment = true,
                    ExclusiveAddressUse = false
                }
                };

                _logger.LogInformation($"UDP listener started on port {_udpPort}");
                _ = AcceptUdpClientsAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Failed to start UDP service: {ex.Message}", ex);
                throw;
            }
        }

        public void Stop()
        {
            if (_isDisposed)
                return;

            try
            {
                _cancellationTokenSource.Cancel();
                _cleanupTimer.Dispose();
                _udpListener?.Dispose();
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

        private async Task AcceptUdpClientsAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("UDP client acceptance loop started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _connectionSemaphore.WaitAsync(cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var receiveResult = await _udpListener!.ReceiveAsync()
                        .ConfigureAwait(false);

                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _logger.LogDebug($"New client connected: {receiveResult.RemoteEndPoint}, ClientId: {clientId}");

                    var context = new ClientEncryptionContext
                    {
                        ClientId = clientId,
                        RemoteEndPoint = receiveResult.RemoteEndPoint,
                        ConnectionTime = DateTime.UtcNow
                    };

                    _clientContexts[clientId] = context;

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

                    if (_connectionSemaphore.CurrentCount < _maxConnections)
                        _connectionSemaphore.Release();

                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
            }

            _logger.LogTrace("UDP client acceptance loop stopped");
        }

        private async Task HandleUdpDataAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (data.Length < 1)
            {
                _logger.LogWarning($"Invalid data from client {clientId}: empty or null");
                return;
            }

            try
            {
                var protocol = (EncryptionProtocol)data[0];
                _logger.LogDebug($"Client {clientId} using protocol: {protocol}");

                var context = _clientContexts.GetOrAdd(clientId, id => new ClientEncryptionContext
                {
                    ClientId = id,
                    RemoteEndPoint = remoteEndPoint,
                    ConnectionTime = DateTime.UtcNow
                });

                context.LastUsedProtocol = protocol;
                context.LastActivityTime = DateTime.UtcNow;

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

        private async Task HandlePlaintextDataAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (data.Length < 2)
            {
                _logger.LogWarning($"Invalid plaintext data from client {clientId}");
                return;
            }

            try
            {
                var message = Encoding.UTF8.GetString(data, 1, data.Length - 1);
                _logger.LogInformation($"Received plaintext from {remoteEndPoint}: {message}");

                var response = Encoding.UTF8.GetBytes($"Server received plaintext: {message}");
                await SendEncryptedResponseAsync(clientId, remoteEndPoint, response, EncryptionProtocol.AesGcm, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling plaintext: {ex.Message}", ex);
            }
        }

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
                var encryptedData = new byte[data.Length - 1];
                Array.Copy(data, 1, encryptedData, 0, encryptedData.Length);

                var encryptionService = factory(_masterKey);
                var decryptedData = await encryptionService.DecryptAsync(encryptedData).ConfigureAwait(false);
                var message = Encoding.UTF8.GetString(decryptedData);
                _logger.LogInformation($"Decrypted message from {remoteEndPoint} (Protocol {protocol}): {message}");

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

        private async Task SendEncryptedResponseAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, EncryptionProtocol protocol, CancellationToken cancellationToken)
        {
            if (!_encryptionFactories.TryGetValue(protocol, out var factory))
            {
                _logger.LogWarning($"Cannot send response: unsupported protocol {protocol}");
                return;
            }

            try
            {
                var encryptionService = factory(_masterKey);
                var encryptedData = await encryptionService.EncryptAsync(data).ConfigureAwait(false);

                var fullPacket = ArrayPool<byte>.Shared.Rent(1 + encryptedData.Length);
                try
                {
                    fullPacket[0] = (byte)protocol;
                    Array.Copy(encryptedData, 0, fullPacket, 1, encryptedData.Length);

                    await _udpListener!.SendAsync(fullPacket, 1 + encryptedData.Length, remoteEndPoint)
                        .ConfigureAwait(false);

                    _logger.LogDebug($"Sent encrypted response to {remoteEndPoint}, Protocol: {protocol}");
                }
                finally
                {
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

        private async Task SendProtocolErrorAsync(uint clientId, IPEndPoint remoteEndPoint, string message, CancellationToken cancellationToken)
        {
            try
            {
                var errorData = Encoding.UTF8.GetBytes($"[PROTOCOL_ERROR] {message}");
                var packet = new byte[1 + errorData.Length];
                packet[0] = (byte)EncryptionProtocol.None;
                Array.Copy(errorData, 0, packet, 1, errorData.Length);

                await _udpListener!.SendAsync(packet, packet.Length, remoteEndPoint)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to send protocol error: {ex.Message}");
            }
        }

        private async Task SendGeneralErrorAsync(uint clientId, IPEndPoint remoteEndPoint, string message, CancellationToken cancellationToken)
        {
            try
            {
                var errorData = Encoding.UTF8.GetBytes($"[ERROR] {message}");
                var packet = new byte[1 + errorData.Length];
                packet[0] = (byte)EncryptionProtocol.None;
                Array.Copy(errorData, 0, packet, 1, errorData.Length);

                await _udpListener!.SendAsync(packet, packet.Length, remoteEndPoint)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to send general error: {ex.Message}");
            }
        }

        private void RemoveClientContext(uint clientId)
        {
            if (_clientContexts.TryRemove(clientId, out _))
                _logger.LogDebug($"Removed client context: {clientId}");
        }

        private void CleanupExpiredContexts(object? state)
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredIds = _clientContexts
                    .Where(kv => (now - kv.Value.LastActivityTime).TotalMinutes > 5)
                    .Select(kv => kv.Key)
                    .ToList();

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

        private byte[] GenerateSecureMasterKey()
        {
            using var rng = RandomNumberGenerator.Create();
            var key = new byte[32];
            rng.GetBytes(key);
            return key;
        }
    }
    

    /// <summary>
    /// 客户端加密上下文，包含更多状态信息
    /// </summary>
    public class ClientEncryptionContext
    {
        public uint ClientId { get; set; }
        public IPEndPoint RemoteEndPoint { get; set; }
        public EncryptionProtocol LastUsedProtocol { get; set; }
        public DateTime ConnectionTime { get; set; }
        public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
    }
}