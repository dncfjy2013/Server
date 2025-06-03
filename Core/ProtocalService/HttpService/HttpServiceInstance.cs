using Server.Core.Common;
using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Core.ProtocalService.HttpService
{
    // HTTP服务实现
    public class HttpService : IHttpService
    {
        private readonly HttpListener _listener;
        private readonly ILogger _logger;
        private readonly RequestHandler _requestHandler;
        private readonly ConnectionManager _connectionManager;
        private bool _isRunning;
        private uint _nextClientId;

        public bool IsRunning => _isRunning;
        public string Host { get; }

        public HttpService(ILogger logger, string host, ref uint nextClientId, RequestHandler requestHandler, ConnectionManager connectionManager)
        {
            _logger = logger;
            _nextClientId = nextClientId;
            _listener = new HttpListener();
            Host = host;
            _listener.Prefixes.Add(host);
            _requestHandler = requestHandler;
            _connectionManager = connectionManager;
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                _isRunning = true;
                _logger.LogInformation($"HTTP server started on {Host}");
                Task.Run(() => AcceptClients());
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Failed to start HTTP server: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            _listener?.Close();
            _logger.LogInformation($"HTTP server stopped");
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
                    _logger.LogDebug($"HTTP client connected: {context.Request.RemoteEndPoint}");

                    _ = _requestHandler.HandleHttpRequest(context, clientId);
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 995)
                {
                    _logger.LogTrace("HTTP listener stopped gracefully");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"HTTP accept error: {ex.Message}");
                    await Task.Delay(100);
                }
            }
        }
    }
}