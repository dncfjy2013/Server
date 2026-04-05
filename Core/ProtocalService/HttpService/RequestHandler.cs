using Logger;
using Server.Core.Common;
using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Core.ProtocalService.HttpService
{
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
                using var response = context.Response;
                var responseContent = ProcessHttpRequest(context.Request);
                await WriteResponse(response, responseContent);

                _connectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling HTTP request: {ex.Message}");
                SendErrorResponse(context.Response, 500);
            }
        }

        public async Task HandleHttpsRequest(HttpListenerContext context, uint clientId, X509Certificate2 clientCert)
        {
            try
            {
                using var response = context.Response;
                var responseContent = ProcessHttpsRequest(context.Request, clientCert);
                await WriteResponse(response, responseContent);

                _connectionManager.TryGetClientById(clientId)?.ConnectCompleteAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling HTTPS request: {ex.Message}");
                SendErrorResponse(context.Response, 500);
            }
        }

        private string ProcessHttpRequest(HttpListenerRequest request)
        {
            return request.HttpMethod switch
            {
                "GET" => HandleHttpGet(request),
                "POST" => HandleHttpPost(request),
                "PUT" => HandleHttpPut(request),
                "DELETE" => HandleHttpDelete(request),
                _ => "Method not supported"
            };
        }

        private string ProcessHttpsRequest(HttpListenerRequest request, X509Certificate2 clientCert)
        {
            return request.HttpMethod switch
            {
                "GET" => HandleHttpsGet(request, clientCert),
                "POST" => HandleHttpsPost(request),
                "PUT" => HandleHttpsPut(request),
                "DELETE" => HandleHttpsDelete(request),
                _ => "Method not supported"
            };
        }

        private string HandleHttpGet(HttpListenerRequest request) => "HTTP GET response";
        private string HandleHttpPost(HttpListenerRequest request) => "HTTP POST response";
        private string HandleHttpPut(HttpListenerRequest request) => "HTTP PUT response";
        private string HandleHttpDelete(HttpListenerRequest request) => "HTTP DELETE response";

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