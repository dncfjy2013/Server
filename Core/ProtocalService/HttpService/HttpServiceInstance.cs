using Server.Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Core.ProtocalService.HttpService
{
    public class HttpServiceInstance
    {
        // 用于 HttpListener 连接的监听器，负责监听 Http 端口的客户端连接请求
        private HttpListener _httpListener;
        private ILogger _logger;
        // 服务器运行状态标志，当为 true 时表示服务器正在运行，可接受客户端连接；为 false 时则停止服务
        private bool _isRunning;
        private readonly string _host;
        /// <summary>
        /// 客户端ID生成器（原子递增）
        /// </summary>
        private uint _nextClientId;
        private ConnectionManager _ClientConnectionManager;

        public HttpServiceInstance(ILogger logger, ref bool isRunning, string host, ref uint nextClientId, ConnectionManager clientConnectionManager)
        {
            _logger = logger;
            _isRunning = isRunning;
            _host = host;
            _nextClientId = nextClientId;
            _ClientConnectionManager = clientConnectionManager;
        }

        public void Start()
        {
            _logger.LogDebug($"Starting to create an HTTP listener.");
            _httpListener = new HttpListener();
            _logger.LogDebug($"HTTP listener has been created.");

            _logger.LogDebug($"Starting the HTTP listener on {_host}.");
            _httpListener.Prefixes.Add(_host);
            _httpListener.Start();
            _logger.LogInformation($"HTTP server has started listening on {_host}.");

            _logger.LogDebug($"Starting to accept HTTP clients.");
            AcceptHttpClients();
            _logger.LogDebug("Accepting HTTP clients process has been initiated.");
        }

        public void Stop() 
        {
            if (_httpListener != null)
            {
                _httpListener.Stop();
                _httpListener.Close();
                //_httpListener.Dispose();
                _logger.LogDebug("HttpListener has been stopped and disposed.");
            }
            else
            {
                _logger.LogTrace("The _httpListener listener is null, no disposal operation is required.");
            }
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

        private async Task HandleHttpClient(HttpListenerContext context)
        {
            if (context == null)
            {
                _logger.LogError($"HTTP context is null. Unable to handle the request.");
                return;
            }

            _logger.LogDebug($"Handling HTTP client with request: {context.Request.Url}");

            try
            {
                using (var response = context.Response)
                {
                    // 设置响应的内容类型
                    response.ContentType = "text/html; charset=utf-8";

                    string requestContent = "";
                    string responseString = "";

                    switch (context.Request.HttpMethod)
                    {
                        case "GET":
                            // 处理 GET 请求
                            responseString = HandleGetRequest(context.Request);
                            break;
                        case "POST":
                            // 读取请求内容
                            requestContent = await ReadRequestContentAsync(context.Request);
                            _logger.LogDebug($"Received request content: {requestContent}");
                            // 处理 POST 请求
                            responseString = HandlePostRequest(requestContent);
                            break;
                        case "PUT":
                            // 读取请求内容
                            requestContent = await ReadRequestContentAsync(context.Request);
                            _logger.LogDebug($"Received request content: {requestContent}");
                            // 处理 PUT 请求
                            responseString = HandlePutRequest(requestContent);
                            break;
                        case "DELETE":
                            // 处理 DELETE 请求
                            responseString = HandleDeleteRequest(context.Request);
                            break;
                        default:
                            response.StatusCode = 405; // 不支持的方法
                            responseString = "HTTP method not supported.";
                            break;
                    }

                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);

                    response.ContentLength64 = buffer.Length;
                    using (var output = response.OutputStream)
                    {
                        await output.WriteAsync(buffer, 0, buffer.Length);
                        _logger.LogInformation($"Sent response to HTTP");
                    }
                }
            }
            catch (HttpListenerException httpEx)
            {
                _logger.LogError($"HTTP listener error while handling: {httpEx.Message}");
            }
            catch (IOException ioEx)
            {
                _logger.LogError($"I/O error while handling: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Unexpected error while handling: {ex.Message}");
            }
            finally
            {
                _logger.LogInformation($"HTTP disconnected");
            }
        }

        private string HandleGetRequest(HttpListenerRequest request)
        {
            // 这里添加处理 GET 请求的逻辑
            return "This is a GET request response.";
        }

        private string HandlePostRequest(string requestContent)
        {
            // 这里添加处理 POST 请求的逻辑
            return $"This is a POST request response. Received content: {requestContent}";
        }

        private string HandlePutRequest(string requestContent)
        {
            // 这里添加处理 PUT 请求的逻辑
            return $"This is a PUT request response. Received content: {requestContent}";
        }

        private string HandleDeleteRequest(HttpListenerRequest request)
        {
            // 这里添加处理 DELETE 请求的逻辑
            return "This is a DELETE request response.";
        }

        private async Task<string> ReadRequestContentAsync(HttpListenerRequest request)
        {
            using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

    }
}
