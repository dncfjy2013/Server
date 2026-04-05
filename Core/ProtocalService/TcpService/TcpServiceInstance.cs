using Core.Message;
using Logger;
using Protocol;
using Server.Core.Certification;
using Server.Core.Common;
using Server.Core.Config;
using Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Core.ProtocalService.TcpService
{
    public class TcpServiceInstance
    {
        private readonly int _port;
        private readonly int _sslPort;
        private readonly X509Certificate2 _serverCert;
        private Socket _listener;
        private TcpListener _sslListener;
        private bool _isRunning;

        private readonly ILogger _logger;
        private readonly SSLManager _SSLManager;
        private readonly ConnectionManager _ClientConnectionManager;
        private readonly InMessage _InmessageManager;
        private readonly OutMessage _outMessageManager;
        private uint _nextClientId;
        private int _connectSocket, _connectSSL;
        private readonly ConcurrentDictionary<uint, ClientConfig> _clients;
        private bool _isRealTimeTransferAllowed;
        private bool _isReceiving = true;
        private readonly ConcurrentDictionary<uint, ClientConfig> _historyclients;
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

            _heartbeatTimer = new Timer(_ => CheckHeartbeats(), null, 0, ConstantsConfig.HeartbeatInterval);
        }

        public void Start()
        {
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
            _listener.Listen(ConstantsConfig.ListenMax);
            _logger.Info($"TCP server listening on port {_port}");

            AcceptSocketClients();

            if (_serverCert != null)
            {
                _sslListener = new TcpListener(IPAddress.Any, _sslPort);
                _sslListener.Start();
                _logger.Info($"SSL server listening on port {_sslPort}");

                AcceptSslClients();
                _SSLManager = new SSLManager(_logger);
            }
            else
            {
                _logger.Warn("No SSL certificate provided, SSL disabled");
            }
        }

        public void Stop()
        {
            _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);

            foreach (var client in _clients.Values)
            {
                try
                {
                    DisconnectClient(client.Id);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Disconnect client {client.Id} failed: {ex.Message}");
                }
            }

            _listener?.Dispose();
            _sslListener?.Stop();
            _logger.Info("TCP service stopped successfully");
        }

        private async void AcceptSslClients()
        {
            while (_isRunning)
            {
                try
                {
                    var sslClient = await _sslListener.AcceptTcpClientAsync();
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _ClientConnectionManager.CreateClient(clientId).ConnectAsync();

                    var sslStream = new SslStream(sslClient.GetStream(), false, _SSLManager.ValidateClientCertificate);
                    var sslOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCert,
                        ClientCertificateRequired = true,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    };

                    await sslStream.AuthenticateAsServerAsync(sslOptions);
                    var client = new ClientConfig(clientId, sslStream);
                    _clients.TryAdd(clientId, client);

                    _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
                    Interlocked.Increment(ref _connectSSL);

                    _logger.Info($"SSL client {clientId} connected: {sslClient.Client.RemoteEndPoint}");
                    _ = HandleClient(client);
                }
                catch (AuthenticationException ex)
                {
                    _logger.Critical($"SSL auth failed: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.Critical($"SSL accept error: {ex.Message}, retrying...");
                    await Task.Delay(100);
                }
            }
        }

        private async void AcceptSocketClients()
        {
            while (_isRunning)
            {
                try
                {
                    var clientSocket = await _listener.AcceptAsync();
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _ClientConnectionManager.CreateClient(clientId).ConnectAsync();

                    var client = new ClientConfig(clientId, clientSocket);
                    _clients.TryAdd(clientId, client);

                    _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
                    Interlocked.Increment(ref _connectSocket);

                    _logger.Info($"TCP client {clientId} connected: {clientSocket.RemoteEndPoint}");
                    _ = HandleClient(client);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Critical($"TCP accept error: {ex.Message}, retrying...");
                    await Task.Delay(100);
                }
            }
        }

        private void CheckHeartbeats()
        {
            var now = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(ConstantsConfig.TimeoutSeconds);

            foreach (var client in _clients.ToList())
            {
                if (now - client.Value.LastActivity > timeout)
                {
                    _logger.Warn($"Client {client.Key} heartbeat timeout, disconnecting");
                    DisconnectClient(client.Key);
                    _clients.TryRemove(client.Key, out _);
                }
            }
        }

        private async Task HandleClient(ClientConfig client)
        {
            try
            {
                var stream = client.Socket != null ? new NetworkStream(client.Socket) : client.SslStream;

                while (_isRunning && _isReceiving)
                {
                    byte[] headerBuffer = new byte[8];
                    if (!await ReadFullAsync(stream, headerBuffer, 8)) break;

                    if (!ProtocolHeaderExtensions.TryFromBytes(headerBuffer, out var header)) continue;
                    if (!ConstantsConfig.config.SupportedVersions.Contains((byte)header.Version)) continue;

                    byte[] payloadBuffer = new byte[header.MessageLength];
                    if (!await ReadFullAsync(stream, payloadBuffer, (int)header.MessageLength)) break;

                    byte[] fullPacket = new byte[8 + header.MessageLength];
                    Buffer.BlockCopy(headerBuffer, 0, fullPacket, 0, 8);
                    Buffer.BlockCopy(payloadBuffer, 0, fullPacket, 8, (int)header.MessageLength);

                    var (success, packet, error) = ProtocolPacketWrapper.TryFromBytes(fullPacket);
                    if (!success) continue;

                    client.UpdateActivity();
                    client.SetValue(packet.Data.Sourceid);

                    if (IsVideoOrVoiceRequest(packet.Data))
                    {
                        var targetClient = _clients.Values.FirstOrDefault(c => c.UniqueId == packet.Data.Targetid);
                        if (targetClient != null)
                        {
                            if (!_isRealTimeTransferAllowed)
                            {
                                await _outMessageManager.SendInfoDate(targetClient, packet.Data);
                                continue;
                            }
                            await EstablishDirectConnection(client, targetClient);
                            continue;
                        }
                        _logger.Warn($"Target client {packet.Data.Targetid} not found");
                        continue;
                    }

                    var message = new ClientMessage
                    {
                        Client = client,
                        Data = packet.Data,
                        ReceivedTime = DateTime.Now
                    };

                    if (ConstantsConfig.IsUnityServer)
                    {
                        bool isQueueFull = _InmessageManager._messageHighQueue.Reader.Count > ConstantsConfig.MaxQueueSize ||
                                           _InmessageManager._messageMediumQueue.Reader.Count > ConstantsConfig.MaxQueueSize ||
                                           _InmessageManager._messagelowQueue.Reader.Count > ConstantsConfig.MaxQueueSize;

                        if (isQueueFull && message.Data.Priority == DataPriority.Low)
                        {
                            _logger.Critical($"Discarded low-priority message (queue full)");
                            continue;
                        }

                        switch (packet.Data.Priority)
                        {
                            case DataPriority.Low:
                                await _InmessageManager._messagelowQueue.Writer.WriteAsync(message);
                                break;
                            case DataPriority.High:
                                await _InmessageManager._messageHighQueue.Writer.WriteAsync(message);
                                break;
                            case DataPriority.Medium:
                                await _InmessageManager._messageMediumQueue.Writer.WriteAsync(message);
                                break;
                        }

                        await MonitorQueueBackpressure(client, packet.Data.Priority, (int)header.MessageLength);
                    }
                    else
                    {
                        _InmessageManager.ProcessMessageWithPriority(message, message.Data.Priority);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Critical($"Client {client.Id} error: {ex.Message}");
            }
            finally
            {
                DisconnectClient(client.Id);
                _logger.Info($"Client {client.Id} disconnected");
            }
        }

        private async Task MonitorQueueBackpressure(ClientConfig client, DataPriority priority, int messageSize)
        {
            switch (priority)
            {
                case DataPriority.Low:
                    if (_InmessageManager._messagelowQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.Critical($"Low queue backpressure, pausing 1s");
                        await ImplementBackpressure(client, TimeSpan.FromSeconds(1));
                    }
                    break;
                case DataPriority.Medium:
                    if (_InmessageManager._messageMediumQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.Critical($"Medium queue backpressure, pausing 600ms");
                        await ImplementBackpressure(client, TimeSpan.FromMilliseconds(600));
                    }
                    break;
                case DataPriority.High:
                    if (_InmessageManager._messageHighQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.Critical($"High queue backpressure, pausing 200ms");
                        await ImplementBackpressure(client, TimeSpan.FromMilliseconds(200));
                    }
                    break;
            }
        }

        private async Task ImplementBackpressure(ClientConfig client, TimeSpan delay)
        {
            _isReceiving = false;
            await Task.Delay(delay);
            _isReceiving = true;
        }

        private bool IsVideoOrVoiceRequest(CommunicationData data)
        {
            return data.InfoType == InfoType.CtcVideo || data.InfoType == InfoType.CtcVoice;
        }

        private async Task EstablishDirectConnection(ClientConfig client1, ClientConfig client2)
        {
            try
            {
                using var stream1 = new NetworkStream(client1.Socket);
                using var stream2 = new NetworkStream(client2.Socket);
                await Task.WhenAll(CopyStreamAsync(stream1, stream2), CopyStreamAsync(stream2, stream1));
            }
            catch (Exception ex)
            {
                _logger.Error($"Direct connection error: {ex.Message}");
            }
        }

        private async Task CopyStreamAsync(Stream source, Stream destination)
        {
            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead);
            }
        }

        public void ModifyRealTimeTransfer(bool value)
        {
            _isRealTimeTransferAllowed = value;
        }

        private async Task<bool> ReadFullAsync(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset);
                if (read == 0) return false;
                offset += read;
            }
            return true;
        }

        private async void DisconnectClient(uint clientId)
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                try
                {
                    await _ClientConnectionManager.TryGetClientById(clientId)?.DisconnectAsync();

                    if (client.Socket != null)
                    {
                        Interlocked.Decrement(ref _connectSocket);
                        client.Socket.Shutdown(SocketShutdown.Both);
                    }
                    else if (client.SslStream != null)
                    {
                        Interlocked.Decrement(ref _connectSSL);
                        client.SslStream.Close();
                    }

                    client.IsConnect = false;
                    await _ClientConnectionManager.TryGetClientById(clientId)?.DisconnectCompleteAsync();
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Close client {clientId} error: {ex.Message}");
                }
                finally
                {
                    client.Socket?.Dispose();
                }

                var totalRec = client.BytesReceived + client.FileBytesReceived;
                var totalSent = client.BytesSent + client.FileBytesSent;
                _logger.Warn($"Client {clientId} traffic: Recv {Function.FormatBytes(totalRec)}, Send {Function.FormatBytes(totalSent)}");

                _historyclients.TryAdd(clientId, client);
            }
        }
    }
}