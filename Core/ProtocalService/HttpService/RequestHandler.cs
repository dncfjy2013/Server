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
    // 请求处理器 - 修改HandleHttpsRequest方法签名
    public class RequestHandler
    {
        private readonly ILogger _logger;
        private readonly ConnectionManager _connectionManager;

        public RequestHandler(ILogger logger, ConnectionManager connectionManager)
        {
            _logger = logger;
            _connectionManager = connectionManager;
        }

        public async Task HandleHttpRequest(HttpListenerContext context, uint clientId)
        {
            try
            {
                _logger.LogDebug($"Handling HTTP request: {context.Request.Url}");

                using var response = context.Response;
                var responseContent = ProcessHttpRequest(context.Request);

                await WriteResponse(response, responseContent);

                _connectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
                _logger.LogInformation($"HTTP request handled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling HTTP request: {ex.Message}");
                SendErrorResponse(context.Response, 500);
            }
        }

        // 修改方法签名，添加clientCert参数
        public async Task HandleHttpsRequest(HttpListenerContext context, uint clientId, X509Certificate2 clientCert)
        {
            try
            {
                _logger.LogDebug($"Handling HTTPS request: {context.Request.Url}");

                // 客户端证书已在HttpsService中验证过
                if (clientCert != null)
                {
                    _logger.LogDebug($"Client certificate: {clientCert.Subject}");
                }

                using var response = context.Response;
                var responseContent = ProcessHttpsRequest(context.Request, clientCert);

                await WriteResponse(response, responseContent);

                _connectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
                _logger.LogInformation($"HTTPS request handled successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling HTTPS request: {ex.Message}");
                SendErrorResponse(context.Response, 500);
            }
        }

        private string ProcessHttpRequest(HttpListenerRequest request)
        {
            switch (request.HttpMethod)
            {
                case "GET": return HandleHttpGet(request);
                case "POST": return HandleHttpPost(request);
                case "PUT": return HandleHttpPut(request);
                case "DELETE": return HandleHttpDelete(request);
                default: return "Method not supported";
            }
        }

        private string ProcessHttpsRequest(HttpListenerRequest request, X509Certificate2 clientCert)
        {
            switch (request.HttpMethod)
            {
                case "GET": return HandleHttpsGet(request, clientCert);
                case "POST": return HandleHttpsPost(request);
                case "PUT": return HandleHttpsPut(request);
                case "DELETE": return HandleHttpsDelete(request);
                default: return "Method not supported";
            }
        }

        // HTTP方法处理实现
        private string HandleHttpGet(HttpListenerRequest request) => "HTTP GET response";
        private string HandleHttpPost(HttpListenerRequest request) => "HTTP POST response";
        private string HandleHttpPut(HttpListenerRequest request) => "HTTP PUT response";
        private string HandleHttpDelete(HttpListenerRequest request) => "HTTP DELETE response";

        // HTTPS方法处理实现
        private string HandleHttpsGet(HttpListenerRequest request, X509Certificate2 clientCert)
        {
            var certInfo = clientCert != null ? $"Client cert: {clientCert.Subject}" : "No client certificate";
            return $"HTTPS GET response. {certInfo}";
        }

        private string HandleHttpsPost(HttpListenerRequest request) => "HTTPS POST response";
        private string HandleHttpsPut(HttpListenerRequest request) => "HTTPS PUT response";
        private string HandleHttpsDelete(HttpListenerRequest request) => "HTTPS DELETE response";

        private async Task WriteResponse(HttpListenerResponse response, string content)
        {
            response.ContentType = "text/plain; charset=utf-8";
            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        private void SendErrorResponse(HttpListenerResponse response, int statusCode)
        {
            response.StatusCode = statusCode;
            response.Close();
        }
    }
}
