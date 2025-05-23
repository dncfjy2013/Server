﻿using System.Collections.Concurrent;
using System.Net.Security;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Server.Utils;
using Server.Core.Config;
using Server.Core.Common;
using MySqlX.XDevAPI;

namespace Server.Core
{
    partial class ServerInstance
    { 
        /// <summary>
        /// 客户端连接字典（线程安全），键为客户端ID，值为客户端配置对象
        /// </summary>
        private readonly ConcurrentDictionary<uint, ClientConfig> _clients = new();

        private readonly ConcurrentDictionary<uint  , ClientConfig> _historyclients = new();

        /// <summary>
        /// 客户端ID生成器（原子递增）
        /// </summary>
        private uint _nextClientId;


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

        private async void AcceptHttpClients()
        {
            _logger.LogTrace("Enter AcceptHttpClients loop");

            while (_isRunning)
            {
                try
                {
                    // 接受 HTTP 请求
                    var context = await _httpListener.GetContextAsync();
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _ClientConnectionManager.CreateClient(clientId).ConnectAsync();
                    _logger.LogDebug($"Accepted new HTTP client: {context.Request.RemoteEndPoint}");

                    _logger.LogInformation($"HTTP client connected: {context.Request.RemoteEndPoint}");

                    _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
                    // 启动客户端消息处理任务
                    _ = HandleHttpClient(context);
                    _logger.LogDebug($"Started HandleHttpClient task for HTTP client");
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                {
                    // 正常关闭时忽略（如服务器 Stop 调用）
                    _logger.LogTrace("HTTP listener interrupted (expected shutdown)");
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"HTTP Accept error: {ex.Message}, {ex}");
                    _logger.LogWarning($"Retrying HTTP accept in 100ms...");
                    await Task.Delay(100);
                }
            }

            _logger.LogTrace("Exited AcceptHttpClients loop (server stopped)");
        }

        private async void AcceptUdpClients()
        {
            _logger.LogTrace("Enter AcceptUdpClients loop");

            while (_isRunning)
            {
                try
                {
                    var result = await _udpListener.ReceiveAsync();

                    var clientId = Interlocked.Increment(ref _nextClientId);
                    _ClientConnectionManager.CreateClient(clientId).ConnectAsync();

                    var remoteEndPoint = result.RemoteEndPoint;
                    var data = result.Buffer;
                    _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
                    _logger.LogDebug($"Received UDP data from: {remoteEndPoint}");

                    // 处理 UDP 数据
                    _ = HandleUdpData(remoteEndPoint, data);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"UDP receive error: {ex.Message}");
                    _logger.LogWarning($"Retrying UDP receive in 100ms...");
                    await Task.Delay(100);
                }
            }

            _logger.LogTrace("Exited AcceptUdpClients loop (server stopped)");
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

                if(_historyclients.TryAdd(clientId, client))
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
