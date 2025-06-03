using Server.Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Core.ProtocalService.HttpService
{
    // HTTPS服务实现
    public class HttpsService : IHttpService
    {
        private readonly HttpListener _listener;
        private readonly ILogger _logger;
        private readonly RequestHandler _requestHandler;
        private readonly ConnectionManager _connectionManager;
        private readonly string _certificatePath;
        private readonly string _certificatePassword;
        private readonly string _trustedCertPath;
        private bool _isRunning;
        private uint _nextClientId;
        private X509Certificate2 _serverCertificate;
        private X509Certificate2 _trustedCertificate;

        public bool IsRunning => _isRunning;
        public string Host { get; }

        public HttpsService(ILogger logger, string host, ref uint nextClientId, string certificatePath, string certificatePassword,
            string trustedCertPath, RequestHandler requestHandler, ConnectionManager connectionManager)
        {
            _logger = logger;
            _listener = new HttpListener();
            _nextClientId = nextClientId;
            Host = host;
            _listener.Prefixes.Add(host);
            _certificatePath = certificatePath;
            _certificatePassword = certificatePassword;
            _trustedCertPath = trustedCertPath;
            _requestHandler = requestHandler;
            _connectionManager = connectionManager;
        }

        public void Start()
        {
            try
            {
                LoadCertificates();
                _listener.Start();
                _isRunning = true;
                _logger.LogInformation($"HTTPS server started on {Host}");
                Task.Run(() => AcceptClients());
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Failed to start HTTPS server: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
            _logger.LogInformation($"HTTPS server stopped");
        }

        private void LoadCertificates()
        {
            try
            {
                _serverCertificate = new X509Certificate2(_certificatePath, _certificatePassword);
                _logger.LogInformation($"HTTPS server certificate loaded: {_serverCertificate.Subject}");

                if (!string.IsNullOrEmpty(_trustedCertPath))
                {
                    _trustedCertificate = new X509Certificate2(_trustedCertPath);
                    _logger.LogInformation($"HTTPS trusted certificate loaded: {_trustedCertificate.Subject}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Failed to load certificate: {ex.Message}");
                throw;
            }
        }

        private async Task AcceptClients()
        {
            while (_isRunning)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    var clientId = Interlocked.Increment(ref _nextClientId);

                    _connectionManager.CreateClient(clientId).ConnectAsync();
                    _logger.LogDebug($"HTTPS client connected: {context.Request.RemoteEndPoint}");

                    // 验证客户端证书
                    var clientCert = GetClientCertificate(context);
                    bool isCertValid = ValidateClientCertificate(clientCert);

                    if (isCertValid || _trustedCertificate == null)
                    {
                        _ = _requestHandler.HandleHttpsRequest(context, clientId, clientCert);
                    }
                    else
                    {
                        _logger.LogWarning("Client certificate validation failed");
                        SendErrorResponse(context.Response, 403);
                        _connectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
                    }
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                {
                    _logger.LogTrace("HTTPS listener stopped gracefully");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"HTTPS accept error: {ex.Message}");
                    await Task.Delay(100);
                }
            }
        }

        private bool ValidateClientCertificate(X509Certificate2 clientCert)
        {
            if (clientCert == null || _trustedCertificate == null)
                return false;

            try
            {
                X509Chain chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                chain.ChainPolicy.ExtraStore.Add(_trustedCertificate);

                bool isValid = chain.Build(clientCert);

                _logger.LogDebug($"Client certificate validation result: {isValid}");

                // 额外验证证书链中是否有受信任的根证书
                foreach (X509ChainElement element in chain.ChainElements)
                {
                    if (element.Certificate.Thumbprint == _trustedCertificate.Thumbprint)
                    {
                        _logger.LogDebug("Client certificate chain contains trusted certificate");
                        return true;
                    }
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Certificate validation error: {ex.Message}");
                return false;
            }
        }

        private X509Certificate2 GetClientCertificate(HttpListenerContext context)
        {
            try
            {
                var connectionProperty = context.GetType().GetProperty("Connection");
                if (connectionProperty == null) return null;

                var connection = connectionProperty.GetValue(context);
                if (connection == null) return null;

                var getClientCertMethod = connection.GetType().GetMethod("GetClientCertificate");
                if (getClientCertMethod == null) return null;

                var cert = getClientCertMethod.Invoke(connection, null) as X509Certificate;
                return cert != null ? new X509Certificate2(cert) : null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to get client certificate: {ex.Message}");
                return null;
            }
        }

        private void SendErrorResponse(HttpListenerResponse response, int statusCode)
        {
            response.StatusCode = statusCode;
            response.Close();
        }
    }
}
