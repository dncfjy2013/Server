using Proxy.Common;
using Server.Logger;
using Server.Proxy.Common;
using Server.Proxy.Config;
using Server.Proxy.LoadBalance;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    public sealed class HttpPortForwarder : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly ILoadBalancer _loadBalancer;
        private readonly ConcurrentDictionary<int, HttpListener> _httpListeners = new();
        private readonly ConcurrentDictionary<string, TargetServer> _httpServerMap = new();
        private readonly ConcurrentDictionary<int, RateLimiter> _portLimiters = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private bool _isRunning;
        private bool _disposed;
        private DefaultIpGeoLocationService _ipGeoLocationService;
        private DefaultIpGeoLocationService.Options _options = new()
        {
            CacheSize = 2000,
            CacheExpiry = TimeSpan.FromMinutes(15),
            EnableLogging = true
        };

        public HttpPortForwarder(ILogger logger, ILoadBalancer loadBalancer)
        {
            _logger = logger;
            _loadBalancer = loadBalancer;
            InitIpZone();
        }

        public void Init(IEnumerable<EndpointConfig> endpoints)
        {
            foreach (var ep in endpoints.Where(e =>
                     e.Protocol == ConnectType.Http ||
                     e.Protocol == ConnectType.Https))
            {
                _portLimiters[ep.ListenPort] = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
                {
                    PermitLimit = ep.MaxConnections,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 100
                });
            }
        }

        public async Task StartAsync(IEnumerable<EndpointConfig> endpoints)
        {
            if (_isRunning) throw new InvalidOperationException("HTTP转发器已运行");
            _isRunning = true;
            _logger.LogInformation("启动HTTP端口转发器...");

            var tasks = new List<Task>();
            foreach (var ep in endpoints.Where(e =>
                     e.Protocol == ConnectType.Http ||
                     e.Protocol == ConnectType.Https))
            {
                tasks.Add(RunHttpEndpointAsync(ep, _cancellationTokenSource.Token));
            }

            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { _logger.LogInformation("HTTP转发器已停止"); }
            catch (Exception ex) { _logger.LogCritical($"HTTP转发器启动失败：{ex.Message}"); }
            finally { _isRunning = false; }
        }

        public async Task StopAsync(TimeSpan timeout)
        {
            _logger.LogInformation("开始停止HTTP转发器...");
            _cancellationTokenSource.Cancel();

            var stopTasks = new List<Task>();
            foreach (var listener in _httpListeners.Values)
            {
                stopTasks.Add(Task.Run(() => listener.Stop()));
            }
            await Task.WhenAll(stopTasks);
            _httpListeners.Clear();

            foreach (var limiter in _portLimiters.Values) limiter.Dispose();
            _portLimiters.Clear();

            _logger.LogInformation("HTTP转发器已完全停止");
        }

        public PortForwarderMetrics GetMetrics()
        {
            return new PortForwarderMetrics
            {
                EndpointStatus = _httpListeners.Keys.Select(port => new EndpointStatus
                {
                    ListenPort = port,
                    Protocol = ConnectType.Http,
                    IsActive = _httpListeners.ContainsKey(port)
                }).ToList(),
                ConnectionMetrics = _httpServerMap.Values.Select(server => new ConnectionMetrics
                {
                    Target = $"{server.Ip}:{server.TargetPort}",
                    ActiveConnections = server.CurrentConnections,
                    TotalConnections = server.RequestCount,
                    Http2xxCount = server.Http2xxCount,
                    Http3xxCount = server.Http3xxCount,
                    Http4xxCount = server.Http4xxCount,
                    Http5xxCount = server.Http5xxCount
                }).ToList()
            };
        }

        private void InitIpZone()
        {
            _ipGeoLocationService = new DefaultIpGeoLocationService(_options, _logger);
            AddCustomMappings(_ipGeoLocationService);
            LoadRulesFromFile();
        }

        private void AddCustomMappings(DefaultIpGeoLocationService service)
        {
            var customRules = new (string cidr, string zone)[]
            {
                ("103.21.0.0/13", "cloudflare"),
                ("202.96.0.0/11", "telecom-cn"),
                ("2606:4700::/32", "cloudflare")
            };

            foreach (var (cidr, zone) in customRules)
            {
                service.TryAddMapping(cidr, zone);
            }
        }

        private void LoadRulesFromFile()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ip-rules.txt");
            if (File.Exists(configPath))
            {
                _ipGeoLocationService.LoadFromConfig(configPath);
            }
        }

        private string GetClientZone(HttpListenerContext context, TargetServer target = null)
        {
            // 优先使用目标服务器指定的区域
            if (target != null && !string.IsNullOrEmpty(target.Zone))
            {
                return target.Zone;
            }

            if (context.Request.Headers["X-Client-Zone"] is string zoneHeader)
            {
                return zoneHeader;
            }

            if (context.Request.Headers["CF-IPCountry"] is string country)
            {
                return MapCountryToZone(country);
            }

            var clientIp = context.Request.RemoteEndPoint.Address.ToString();
            return _ipGeoLocationService.GetZoneByIp(clientIp);
        }

        private string MapCountryToZone(string countryCode)
        {
            return countryCode switch
            {
                "CN" => "ap-east",
                "US" => "na-east",
                "JP" => "ap-northeast",
                _ => "unknown"
            };
        }

        private async Task RunHttpEndpointAsync(EndpointConfig ep, CancellationToken ct)
        {
            if (!HttpListener.IsSupported)
            {
                _logger.LogError($"不支持的平台，无法启动HTTP监听：{ep.ListenPort}");
                return;
            }

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://{ep.ListenIp}:{ep.ListenPort}/");
            listener.Start();

            if (!_httpListeners.TryAdd(ep.ListenPort, listener))
            {
                listener.Stop();
                throw new InvalidOperationException($"端口被占用：{ep.ListenPort}");
            }

            _logger.LogInformation($"HTTP监听启动：{ep.ListenIp}:{ep.ListenPort}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    using var lease = await _portLimiters[ep.ListenPort].AcquireAsync(1, ct);
                    if (!lease.IsAcquired)
                    {
                        _logger.LogWarning($"请求限流：{ep.ListenPort}");
                        continue;
                    }

                    try
                    {
                        var context = await listener.GetContextAsync();
                        _ = HandleHttpContextAsync(context, ep, ct);
                    }
                    catch (HttpListenerException ex)
                    {
                        _logger.LogWarning($"HTTP上下文获取失败：{ex.ErrorCode}");
                    }
                }
            }
            finally
            {
                _httpListeners.TryRemove(ep.ListenPort, out _);
                listener.Stop();
            }
        }

        private async Task HandleHttpContextAsync(HttpListenerContext context, EndpointConfig ep, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;
            var requestId = Guid.NewGuid().ToString();
            var stopwatch = Stopwatch.StartNew();
            TargetServer target = null;

            try
            {
                _logger.LogDebug($"HTTP请求 [{requestId}]：{request.HttpMethod} {request.Url}");

                target = ProxyConstant._isUseHttpMap
                    ? _httpServerMap.GetOrAdd(request.RemoteEndPoint.ToString(),
                        _ => _loadBalancer.SelectServerAsync(ep, context).GetAwaiter().GetResult())
                    : await _loadBalancer.SelectServerAsync(ep, context);

                target.Increment(); // 增加活跃连接数

                // 构建目标URI（应用路径配置）
                var targetPath = BuildTargetPath(request, target, ep);
                var targetUri = new Uri($"http://{target.Ip}:{target.TargetPort}{targetPath}");

                using var httpClient = new HttpClient { Timeout = target.Timeout.TimeSpan };
                using var httpReq = new HttpRequestMessage(new HttpMethod(request.HttpMethod), targetUri);

                CopyRequestHeaders(request, httpReq, target);
                if (request.HasEntityBody) await CopyRequestContent(request, httpReq);

                using var httpRes = await httpClient.SendAsync(httpReq,
                    HttpCompletionOption.ResponseHeadersRead, ct);

                await CopyResponseAsync(httpRes, response, target, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"请求取消 [{requestId}]");
                response.StatusCode = 503;
            }
            catch (Exception ex)
            {
                _logger.LogError($"请求处理失败 [{requestId}]：{ex.Message}");
                response.StatusCode = 500;
            }
            finally
            {
                target?.Decrement(); // 减少活跃连接数
                response.Close();
                stopwatch.Stop();
                target?.UpdateResponseTime(stopwatch.ElapsedMilliseconds);
            }
        }

        private string BuildTargetPath(HttpListenerRequest request, TargetServer target, EndpointConfig ep)
        {
            if (target.StripPath)
            {
                // 剥离前缀，使用目标服务器路径
                var path = request.Url.PathAndQuery;
                if (!string.IsNullOrEmpty(ep.PathPrefix) && path.StartsWith(ep.PathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring(ep.PathPrefix.Length);
                }
                return $"{target.HttpPath.TrimEnd('/')}/{path.TrimStart('/')}";
            }
            else
            {
                // 保留完整路径
                return request.Url.PathAndQuery;
            }
        }

        private void CopyRequestHeaders(HttpListenerRequest source, HttpRequestMessage dest, TargetServer target)
        {
            // 添加目标服务器自定义头
            foreach (var header in target.RequestHeaders)
            {
                dest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // 复制原始请求头
            foreach (string header in source.Headers.AllKeys)
            {
                if (header.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    header.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                dest.Headers.TryAddWithoutValidation(header, source.Headers.GetValues(header));
            }

            // 设置正确的Host头
            dest.Headers.Host = $"{target.Ip}:{target.TargetPort}";
        }

        private async Task CopyRequestContent(HttpListenerRequest source, HttpRequestMessage dest)
        {
            dest.Content = new StreamContent(source.InputStream);

            if (!string.IsNullOrEmpty(source.ContentType))
            {
                dest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(source.ContentType);
            }

            if (source.ContentLength64 > 0)
            {
                dest.Content.Headers.ContentLength = source.ContentLength64;
            }
        }

        private async Task CopyResponseAsync(HttpResponseMessage source, HttpListenerResponse dest, TargetServer target, CancellationToken ct)
        {
            dest.StatusCode = (int)source.StatusCode;
            target?.UpdateHttpStatus(dest.StatusCode);

            // 复制响应头
            foreach (var header in source.Headers)
            {
                dest.Headers[header.Key] = string.Join(",", header.Value);
            }

            if (source.Content != null)
            {
                // 复制内容头
                foreach (var header in source.Content.Headers)
                {
                    dest.Headers[header.Key] = string.Join(",", header.Value);
                }

                // 流式复制响应体
                using var responseStream = await source.Content.ReadAsStreamAsync(ct);
                await responseStream.CopyToAsync(dest.OutputStream, ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await StopAsync(TimeSpan.FromSeconds(30));
                _cancellationTokenSource.Dispose();
                _httpListeners.Clear();
            }
        }
    }
}