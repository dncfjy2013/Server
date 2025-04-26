using Server.Extend;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Server.Common;

namespace Server.Core
{
    partial class Server
    {
        private readonly ConcurrentDictionary<int, ClientConfig> _clients = new();
        private int _nextClientId;
        // 创建协议配置（假设已在外部初始化）
        ProtocolConfiguration config = new ProtocolConfiguration
        {
            DataSerializer = new ProtobufSerializerAdapter(),
            ChecksumCalculator = new Crc16Calculator(),
            SupportedVersions = new byte[] { 0x01, 0x02 },
            MaxPacketSize = 128 * 1024 * 1024 // 与接收方配置一致
        };


        // 新增SSL客户端接受方法
        private async Task AcceptSslClients()
        {
            while (_isRunning)
            {
                try
                {
                    var sslClient = await _sslListener.AcceptTcpClientAsync();
                    var sslStream = new SslStream(sslClient.GetStream(), false);

                    // 配置SSL参数
                    var sslOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCert,
                        ClientCertificateRequired = true, // 强制客户端证书验证
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    };

                    await sslStream.AuthenticateAsServerAsync(sslOptions);

                    var clientId = Interlocked.Increment(ref _nextClientId);
                    var client = new ClientConfig(clientId, sslStream);
                    _clients[clientId] = client;

                    logger.LogInformation($"SSL Client {clientId} connected: {sslClient.Client.RemoteEndPoint}");
                    _ = HandleClient(client);
                }
                catch (Exception ex)
                {
                    logger.LogError($"SSL accept error: {ex.Message}");
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

                    var client = new ClientConfig(clientId, clientSocket);
                    _clients[clientId] = client;

                    logger.LogInformation($"Socket Client {clientId} connected: {clientSocket.RemoteEndPoint}");

                    _ = HandleClient(client);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // 正常关闭时忽略异常
                }
                catch (Exception ex)
                {
                    logger.LogError($"Socket Accept error: {ex.Message}");
                }
            }
        }

        private void DisconnectClient(int clientId)
        {

            if (_clients.TryRemove(clientId, out var client))
            {
                try
                {
                    if (client.Socket != null)
                    {
                        client.Socket.Shutdown(SocketShutdown.Both);
                    }
                    else if (client.SslStream != null)
                    {
                        client.SslStream?.Close();
                        client.Socket?.Dispose();
                    }

                    client.IsConnect = false;
                }
                catch { /* 忽略关闭异常 */ }

                client.Socket?.Dispose();

                var totalRec = client.BytesReceived + client.FileBytesReceived;
                var totalSent = client.BytesSent + client.FileBytesSent;
                var totalCountRec = client.ReceiveCount + client.ReceiveFileCount;
                var totalCountSent = client.SendCount + client.SendFileCount;

                string message = $"Client {clientId} disconnected. " +
                                $"Normal: Recv {Function.FormatBytes(client.BytesReceived)} Send {Function.FormatBytes(client.BytesSent)} | " +
                                $"File: Recv {Function.FormatBytes(client.FileBytesReceived)} Send {Function.FormatBytes(client.FileBytesSent)} | " +
                                $"Total: Recv {Function.FormatBytes(totalRec)} Send {Function.FormatBytes(totalSent)} |" +
                                $"Count Normal: Recv {client.ReceiveCount} Send {client.SendCount} | " +
                                $"Count File: Recv {client.ReceiveFileCount} Send {client.SendFileCount} | " +
                                $"Count Total: Recv {totalCountRec} Send {totalCountSent} |";
                logger.LogWarning(message);
            }
        }
    }
}
