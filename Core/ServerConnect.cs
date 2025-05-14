using System.Collections.Concurrent;
using System.Net.Security;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Server.Utils;
using Server.Core.Config;
using Server.Common.Constants;
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

        /// <summary>
        /// 协议全局配置（序列化、校验和、版本支持等）
        /// </summary>
        ProtocolConfiguration config = new ProtocolConfiguration
        {
            DataSerializer = new ProtobufSerializerAdapter(),        // Protobuf序列化器
            ChecksumCalculator = new Crc16Calculator(),              // CRC16校验和计算器
            SupportedVersions = new byte[] { 0x01, 0x02 },           // 支持的协议版本号
            MaxPacketSize = 128 * 1024 * 1024                        // 最大数据包大小（128MB）
        };

        /// <summary>
        /// 异步接受SSL客户端连接（循环执行直到服务器停止）
        /// </summary>
        private async Task AcceptSslClients()
        {
            _logger.LogTrace("Enter AcceptSslClients loop");

            // 加载信任的客户端根证书（如果有）
            X509Certificate2? trustedClientRoot = null;
            try
            {
                // 从文件或存储中加载信任的根证书
                trustedClientRoot = new X509Certificate2("trusted_client_root.cer");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load trusted client root certificate: {ex.Message}");
            }

            while (_isRunning)
            {
                try
                {
                    // 1. 接受SSL客户端连接
                    var sslClient = await _sslListener.AcceptTcpClientAsync();
                    _logger.LogDebug($"Accepted new SSL client: {sslClient.Client.RemoteEndPoint}");

                    // 分配客户端ID并
                    var clientId = Interlocked.Increment(ref _nextClientId);
                    await _ClientConnectionManager.CreateClient(clientId).ConnectAsync(); 

                    // 2. 创建SSL流
                    var sslStream = new SslStream(sslClient.GetStream(), false, ValidateClientCertificate);
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

                        await _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();

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

                        await _ClientConnectionManager.TryGetClientById(clientId)?.ErrorAsync();
                    }
                    catch (Exception ex)
                    {
                        await _ClientConnectionManager.TryGetClientById(clientId)?.ErrorAsync();
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

        // 自定义客户端证书验证回调
        private bool ValidateClientCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            // 基础验证：证书不能为空
            if (certificate == null)
            {
                _logger.LogCritical("Client certificate is null - Authentication failed");
                return false;
            }

            _logger.LogInformation($"Client certificate received: {certificate.Subject}");
            _logger.LogDebug($"Certificate details: Issuer={certificate.Issuer}, Serial={certificate.GetSerialNumberString()}");

            // 开发环境配置：允许自签名证书
            bool isDevelopment = IsDevelopmentEnvironment();
            _logger.LogTrace($"Environment mode: {(isDevelopment ? "Development" : "Production")}");

            // 证书指纹验证
            bool thumbprintValid = ValidateCertificateThumbprint(certificate);
            _logger.LogDebug($"Certificate thumbprint validation: {(thumbprintValid ? "Passed" : "Failed")}");

            if (!thumbprintValid)
            {
                _logger.LogWarning($"Certificate thumbprint mismatch: {certificate.GetCertHashString()}");
                if (!isDevelopment)
                {
                    _logger.LogError("Rejected in non-development environment due to thumbprint mismatch");
                    return false;
                }
                _logger.LogWarning("Allowing certificate with invalid thumbprint in development environment");
            }

            // 处理证书链错误
            if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                if (chain == null)
                {
                    _logger.LogCritical("Certificate chain is null but chain error detected - Authentication failed");
                    return false;
                }

                bool hasNonTrustedRootError = false;
                bool hasCriticalError = false;

                _logger.LogWarning($"Certificate chain validation issues detected: {sslPolicyErrors}");

                foreach (var status in chain.ChainStatus)
                {
                    _logger.LogDebug($"Chain status: {status.Status} - {status.StatusInformation}");

                    // 标记严重错误
                    if (status.Status == X509ChainStatusFlags.InvalidBasicConstraints ||
                        status.Status == X509ChainStatusFlags.InvalidNameConstraints ||
                        status.Status == X509ChainStatusFlags.InvalidPolicyConstraints)
                    {
                        _logger.LogError($"Critical chain error: {status.Status}");
                        hasCriticalError = true;
                    }

                    // 标记非信任根证书错误
                    if (status.Status == X509ChainStatusFlags.UntrustedRoot)
                    {
                        hasNonTrustedRootError = true;
                    }
                }

                // 生产环境下，任何链错误都视为失败
                if (!isDevelopment)
                {
                    _logger.LogError("Certificate chain validation failed in production environment");
                    return false;
                }

                // 开发环境下仅允许非信任根错误
                if (hasCriticalError || !hasNonTrustedRootError || chain.ChainStatus.Length > 1)
                {
                    _logger.LogError("Non-acceptable chain errors detected even in development environment");
                    return false;
                }

                _logger.LogWarning("Accepting certificate with untrusted root (development environment)");
            }

            // 处理其他证书错误
            if ((sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                _logger.LogError($"Certificate validation errors: {sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors}");
                return false;
            }

            // 证书主体白名单验证
            string[] allowedSubjects = { "CN=trusted-client", "CN=api-client" };
            bool subjectAllowed = allowedSubjects.Contains(certificate.Subject);

            _logger.LogDebug($"Certificate subject validation: {(subjectAllowed ? "Allowed" : "Rejected")} - {certificate.Subject}");
            if (!subjectAllowed)
            {
                _logger.LogWarning($"Certificate subject not in allowed list: {certificate.Subject}");
                return false;
            }

            // 证书有效期验证
            DateTime now = DateTime.UtcNow;
            if (DateTime.TryParse(certificate.GetExpirationDateString(), out DateTime expiryDate) &&
                expiryDate < now)
            {
                _logger.LogError($"Certificate has expired: {expiryDate:u} (Current: {now:u})");
                return false;
            }

            _logger.LogInformation("Client certificate validation passed successfully");
            return true;
        }

        // 证书指纹验证方法
        private bool ValidateCertificateThumbprint(X509Certificate certificate)
        {
            using var x509Cert = new X509Certificate2(certificate);
            string certificateHash = BitConverter.ToString(x509Cert.GetCertHash()).Replace("-", "").ToUpperInvariant();

            // 从配置加载允许的指纹列表
            var allowedThumbprints = LoadAllowedThumbprints();

            _logger.LogDebug($"Verifying certificate thumbprint: {certificateHash}");

            bool isValid = allowedThumbprints.Contains(certificateHash);
            if (!isValid)
            {
                _logger.LogDebug($"Thumbprint not in allowed list. Allowed: {string.Join(", ", allowedThumbprints)}");
            }

            return isValid;
        }

        // 从配置加载允许的指纹列表
        private HashSet<string> LoadAllowedThumbprints()
        {
            // 实际应用中应从配置文件或安全存储加载
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "1234567890ABCDEF1234567890ABCDEF12345678",
                "ABCDEF1234567890ABCDEF1234567890ABCDEF12",
                "168F56CD35982FA128EECC2B7247203866E163E3"
            };
        }

        // 判断当前是否为开发环境
        private bool IsDevelopmentEnvironment()
        {
            return ConstantsConfig.IsDevelopment;
        }
        /// <summary>
        /// 异步接受普通Socket客户端连接（循环执行直到服务器停止）
        /// </summary>
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
                    await _ClientConnectionManager.CreateClient(clientId).ConnectAsync();

                    // 2. 创建配置对象
                    var client = new ClientConfig(clientId, clientSocket);
                    _clients.TryAdd(clientId, client);

                    _logger.LogInformation($"Socket Client {clientId} connected: {clientSocket.RemoteEndPoint}");
                    _logger.LogTrace($"Added client {clientId} to _clients (count={_clients.Count})");

                    await _ClientConnectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
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
                    _logger.LogDebug($"Accepted new HTTP client: {context.Request.RemoteEndPoint}");

                    _logger.LogInformation($"HTTP client connected: {context.Request.RemoteEndPoint}");

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
                    var remoteEndPoint = result.RemoteEndPoint;
                    var data = result.Buffer;

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


        /// <summary>
        /// 断开客户端连接并清理资源
        /// </summary>
        /// <param name="clientId">客户端ID</param>
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
                        client.Socket.Shutdown(SocketShutdown.Both);
                        _logger.LogDebug($"Shut down socket for client {clientId}");
                    }
                    else if (client.SslStream != null)
                    {
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
