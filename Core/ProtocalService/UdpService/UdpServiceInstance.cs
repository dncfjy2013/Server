using Logger;
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
            _udpPort = udpPort;
            _connectionManager = connectionManager;
            _nextClientId = nextClientId;
            _maxConnections = 100;
            _masterKey = masterKey ?? GenerateSecureMasterKey();

            _connectionSemaphore = new SemaphoreSlim(_maxConnections, _maxConnections);
            _cleanupTimer = new Timer(CleanupExpiredContexts, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

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

                _logger.Info($"UDP listener started on port {_udpPort}");
                _ = AcceptUdpClientsAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.Critical($"Failed to start UDP service: {ex.Message}", ex);
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

                var count = _clientContexts.Count;
                _clientContexts.Clear();
                _logger.Info($"Stopped UDP service. Cleared {count} client contexts.");
            }
            catch (Exception ex)
            {
                _logger.Warn($"Error stopping UDP service: {ex.Message}");
            }
        }

        private async Task AcceptUdpClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _connectionSemaphore.WaitAsync(cancellationToken);
                    if (cancellationToken.IsCancellationRequested) break;

                    var result = await _udpListener!.ReceiveAsync().ConfigureAwait(false);
                    var clientId = Interlocked.Increment(ref _nextClientId);

                    var context = new ClientEncryptionContext
                    {
                        ClientId = clientId,
                        RemoteEndPoint = result.RemoteEndPoint,
                        ConnectionTime = DateTime.UtcNow
                    };
                    _clientContexts[clientId] = context;

                    _ = HandleUdpDataAsync(clientId, result.RemoteEndPoint, result.Buffer, cancellationToken)
                        .ContinueWith(_ => _connectionSemaphore.Release(), TaskContinuationOptions.OnlyOnRanToCompletion);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
                {
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error accepting UDP client: {ex.Message}", ex);

                    if (_connectionSemaphore.CurrentCount < _maxConnections)
                        _connectionSemaphore.Release();

                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task HandleUdpDataAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (data.Length < 1)
            {
                _logger.Warn($"Invalid data from client {clientId}");
                return;
            }

            try
            {
                var protocol = (EncryptionProtocol)data[0];
                var context = _clientContexts.GetOrAdd(clientId, _ => new ClientEncryptionContext
                {
                    ClientId = clientId,
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
                        _logger.Warn($"Unsupported protocol {protocol} from client {clientId}");
                        await SendProtocolErrorAsync(clientId, remoteEndPoint, "Unsupported protocol", cancellationToken);
                        break;
                }
            }
            catch (CryptographicException cex)
            {
                _logger.Warn($"Crypto error client {clientId}: {cex.Message}");
                await SendProtocolErrorAsync(clientId, remoteEndPoint, "Encryption error", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling client {clientId}: {ex.Message}", ex);
            }
        }

        private async Task HandlePlaintextDataAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (data.Length < 2)
            {
                _logger.Warn($"Invalid plaintext from {clientId}");
                return;
            }

            try
            {
                var msg = Encoding.UTF8.GetString(data, 1, data.Length - 1);
                var resp = Encoding.UTF8.GetBytes($"Server received: {msg}");
                await SendEncryptedResponseAsync(clientId, remoteEndPoint, resp, EncryptionProtocol.AesGcm, cancellationToken);
            }
            catch
            {
            }
        }

        private async Task HandleEncryptedDataAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, CancellationToken cancellationToken)
        {
            if (!_clientContexts.TryGetValue(clientId, out var context))
            {
                _logger.Warn($"No context for client {clientId}");
                return;
            }

            if (!_encryptionFactories.TryGetValue(context.LastUsedProtocol, out var factory))
            {
                await SendProtocolErrorAsync(clientId, remoteEndPoint, "Unsupported protocol", cancellationToken);
                return;
            }

            try
            {
                var encrypted = data.AsSpan(1).ToArray();
                var decrypted = await factory(_masterKey).DecryptAsync(encrypted);
                var msg = Encoding.UTF8.GetString(decrypted);
                var resp = Encoding.UTF8.GetBytes($"Server received: {msg}");

                await SendEncryptedResponseAsync(clientId, remoteEndPoint, resp, context.LastUsedProtocol, cancellationToken);
            }
            catch (CryptographicException)
            {
                await SendProtocolErrorAsync(clientId, remoteEndPoint, "Decryption failed", cancellationToken);
            }
            catch
            {
            }
        }

        private async Task SendEncryptedResponseAsync(uint clientId, IPEndPoint remoteEndPoint, byte[] data, EncryptionProtocol protocol, CancellationToken cancellationToken)
        {
            if (!_encryptionFactories.TryGetValue(protocol, out var factory))
                return;

            try
            {
                var encrypted = await factory(_masterKey).EncryptAsync(data);
                var buffer = ArrayPool<byte>.Shared.Rent(1 + encrypted.Length);

                buffer[0] = (byte)protocol;
                encrypted.CopyTo(buffer.AsSpan(1));

                await _udpListener!.SendAsync(buffer, 1 + encrypted.Length, remoteEndPoint);
                ArrayPool<byte>.Shared.Return(buffer);
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.ConnectionReset)
            {
                RemoveClientContext(clientId);
            }
            catch
            {
            }
        }

        private async Task SendProtocolErrorAsync(uint clientId, IPEndPoint remoteEndPoint, string message, CancellationToken cancellationToken)
        {
            try
            {
                var err = Encoding.UTF8.GetBytes($"[PROTOCOL_ERROR] {message}");
                var packet = new byte[1 + err.Length];
                packet[0] = (byte)EncryptionProtocol.None;
                err.CopyTo(packet.AsSpan(1));
                await _udpListener!.SendAsync(packet, packet.Length, remoteEndPoint);
            }
            catch
            {
            }
        }

        private void RemoveClientContext(uint clientId)
        {
            _clientContexts.TryRemove(clientId, out _);
        }

        private void CleanupExpiredContexts(object? state)
        {
            try
            {
                var expired = _clientContexts
                    .Where(x => (DateTime.UtcNow - x.Value.LastActivityTime).TotalMinutes > 5)
                    .Select(x => x.Key)
                    .ToList();

                foreach (var id in expired)
                    RemoveClientContext(id);
            }
            catch
            {
            }
        }

        private byte[] GenerateSecureMasterKey()
        {
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            return key;
        }
    }

    public class ClientEncryptionContext
    {
        public uint ClientId { get; set; }
        public IPEndPoint RemoteEndPoint { get; set; }
        public EncryptionProtocol LastUsedProtocol { get; set; }
        public DateTime ConnectionTime { get; set; }
        public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
    }
}