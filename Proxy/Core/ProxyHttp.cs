using MySqlX.XDevAPI;
using Proxy.Common;
using Server.Proxy.Common;
using Server.Proxy.Config;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Core
{
    sealed partial class AdvancedPortForwarder
    {
        private readonly ConcurrentDictionary<int, HttpListener> _httpListeners = new(); // HTTP 监听器集合
        private readonly ConcurrentDictionary<string , TargetServer> _httpServerMap = new();

        DefaultIpGeoLocationService.Options _options = new DefaultIpGeoLocationService.Options
        {
            CacheSize = 2000,
            CacheExpiry = TimeSpan.FromMinutes(15),
            EnableLogging = true
        };

        DefaultIpGeoLocationService _ipGeoLocationService;

        private void InitIpZone()
        {
            _ipGeoLocationService = new DefaultIpGeoLocationService(_options, _logger);

            // 3. 添加自定义规则
            _logger.LogInformation("Adding custom mappings...");
            AddCustomMappings(_ipGeoLocationService);

            // 4. 从文件加载规则（如果存在）
            // 获取当前应用程序的基目录（即项目的bin / Debug或bin / Release目录）
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // 构建相对于应用程序基目录的配置文件路径
            const string configFileName = "ip-rules.txt";
            string configPath = Path.Combine(baseDirectory, configFileName);
            if (File.Exists(configPath))
            {
                _logger.LogInformation($"Loading rules from file: {configPath}");
                _ipGeoLocationService.LoadFromConfig(configPath);
            }
            else
            {
                _logger.LogWarning($"Config file not found: {configPath}");
            }
        }

        private void AddCustomMappings(DefaultIpGeoLocationService service)
        {
            var customRules = new (string cidr, string zone)[]
            {
                // IPv4 rules
                ("103.21.0.0/13", "cloudflare"),
                ("202.96.0.0/11", "telecom-cn"),
                ("221.130.0.0/16", "unicom-cn"),
                
                // IPv6 rules
                ("2606:4700::/32", "cloudflare"),
                ("240e:0:0:0::/20", "telecom-cn"),
                ("2408:0:0:0::/20", "unicom-cn"),
                
                // Special cases
                ("192.0.2.0/24", "example-net"), // RFC 5737
                ("2001:db8::/32", "example-net")  // RFC 3849
            };

            foreach (var (cidr, zone) in customRules)
            {
                if (service.TryAddMapping(cidr, zone))
                {
                    _logger.LogDebug($"Added rule: {cidr} → {zone}");
                }
                else
                {
                    _logger.LogWarning($"Failed to add rule: {cidr}");
                }
            }
        }
        /// <summary>
        /// 从HTTP请求中提取客户端区域信息
        /// </summary>
        private string GetClientZone(HttpListenerContext context)
        {
            // 1. 优先从自定义头获取
            if (context.Request.Headers["X-Client-Zone"] is string zoneHeader && !string.IsNullOrEmpty(zoneHeader))
            {
                return zoneHeader;
            }

            // 2. 从CDN头获取（如果通过CDN访问）
            if (context.Request.Headers["CF-IPCountry"] is string cloudflareCountry && !string.IsNullOrEmpty(cloudflareCountry))
            {
                return MapCountryToZone(cloudflareCountry);
            }

            // 3. 从客户端IP解析（使用IP地理位置服务）
            var clientIp = context.Request.RemoteEndPoint.Address.ToString();
            return _ipGeoLocationService.GetZoneByIp(clientIp);
        }
        public string GetZoneByIP(string ip)
        {
            return _ipGeoLocationService.GetZoneByIp(ip);
        }
        /// <summary>
        /// 将国家代码映射到区域
        /// </summary>
        private string MapCountryToZone(string countryCode)
        {
            // 简化示例，实际应使用更全面的映射表
            return countryCode switch
            {
                "US" => "us-east",
                "CA" => "us-east",
                "UK" => "eu-west",
                "DE" => "eu-central",
                "CN" => "ap-southeast",
                _ => "unknown"
            };
        }
        private void UpdateResponseTime(TargetServer target, long elapsedMs)
        {
            // 使用线程安全的方式更新平均响应时间（例如滑动窗口或指数加权平均）
            target.AverageResponseTimeMs =
                (target.AverageResponseTimeMs * target.RequestCount + elapsedMs) /
                (target.RequestCount + 1);

            target.RequestCount++; // 记录请求次数
        }
        #region HTTP协议处理模块
        /// <summary>
        /// 启动HTTP端点监听
        /// 实现细节：
        /// • 检查平台是否支持HttpListener（如Linux需确认）
        /// • 使用前缀路由（Prefixes）配置监听路径（当前实现监听所有路径）
        /// • 异步获取HTTP上下文（支持长连接处理）
        /// </summary>
        private async Task RunHttpEndpointAsync(EndpointConfig ep, CancellationToken ct)
        {
            // 检查平台兼容性（如Windows默认支持，Linux需通过mono或其他方式）
            if (!HttpListener.IsSupported)
            {
                _logger.LogError($"当前平台不支持HTTP监听器，端点 {ep.ListenPort} 启动失败");
                return;
            }

            var listener = new HttpListener();
            // 配置监听前缀（格式：协议://IP:端口/路径，此处监听根路径所有请求）
            listener.Prefixes.Add($"http://{ep.ListenIp}:{ep.ListenPort}/");
            listener.Start();

            // 确保端口唯一监听
            if (!_httpListeners.TryAdd(ep.ListenPort, listener))
            {
                listener.Stop();
                throw new InvalidOperationException($"HTTP端口 {ep.ListenPort} 已被占用");
            }

            _logger.LogInformation($"HTTP监听器启动：端口 {ep.ListenPort}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 获取限流器租约（控制并发请求数，防止DDoS攻击）
                    using var lease = await _portLimiters[ep.ListenPort].AcquireAsync(1, ct);
                    if (!lease.IsAcquired)
                    {
                        _logger.LogWarning($"HTTP端口 {ep.ListenPort} 连接数已满，拒绝请求");
                        continue;
                    }

                    try
                    {
                        // 异步获取HTTP上下文（阻塞直到有请求到达）
                        var context = await listener.GetContextAsync();
                        // 启动独立任务处理请求（支持并行处理多个请求）
                        _ = HandleHttpContextAsync(context, ep, ct);
                    }
                    catch (HttpListenerException ex)
                    {
                        // 处理HTTP协议相关异常（如无效请求格式）
                        _logger.LogWarning($"HTTP上下文获取失败：{ex.ErrorCode} - {ex.Message}");
                    }
                }
            }
            finally
            {
                // 清理资源
                _httpListeners.TryRemove(ep.ListenPort, out _);
                listener.Stop();
                _logger.LogInformation($"HTTP监听器停止：端口 {ep.ListenPort}");
            }
        }

        /// <summary>
        /// 处理HTTP请求转发
        /// 实现流程：
        /// 1. 解析客户端请求（方法、URL、头信息、请求体）
        /// 2. 负载均衡选择目标服务器并构造目标URI
        /// 3. 使用HttpClient转发请求（支持自动处理Keep-Alive）
        /// 4. 复制响应结果回客户端（包含状态码、头信息、响应体）
        /// </summary>
        private async Task HandleHttpContextAsync(HttpListenerContext context, EndpointConfig ep, CancellationToken ct)
        {
            var request = context.Request;
            var response = context.Response;
            var remoteEndPoint = request.RemoteEndPoint.ToString();
            var requestId = Guid.NewGuid().ToString(); // 唯一请求ID（用于分布式追踪）
            var stopwatch = Stopwatch.StartNew(); // 记录请求处理耗时
            TargetServer target = null;

            try
            {
                _logger.LogDebug($"HTTP请求 [{requestId}]：{request.HttpMethod} {request.Url} 来自 {remoteEndPoint}");

                // 负载均衡获取目标服务器
                if (ProxyConstant._isUseHttpMap)
                {
                    target = _httpServerMap.GetOrAdd(remoteEndPoint, _ => SelectServerAsync(ep, context).GetAwaiter().GetResult());
                }
                else
                {
                    target = await SelectServerAsync(ep, context);
                }

                // 构造目标URI（保留原始URL路径和查询参数）
                var targetUri = new Uri($"http://{target.Ip}:{target.TargetPort}{request.RawUrl}");

                using var httpClient = new HttpClient(); // 使用默认HttpClient（内置连接池）
                using var httpRequest = new HttpRequestMessage(new HttpMethod(request.HttpMethod), targetUri);

                // 复制请求头（排除Host头，避免覆盖目标服务器地址）
                foreach (string headerKey in request.Headers.AllKeys)
                {
                    if (string.Equals(headerKey, "Host", StringComparison.OrdinalIgnoreCase))
                        continue; // 由转发器重新设置Host头

                    var headerValues = request.Headers.GetValues(headerKey);
                    if (headerValues != null)
                    {
                        httpRequest.Headers.TryAddWithoutValidation(headerKey, headerValues);
                    }
                }
                httpRequest.Headers.Host = targetUri.Authority; // 设置目标服务器Host头

                // 处理请求体（如果存在）
                if (request.HasEntityBody)
                {
                    httpRequest.Content = new StreamContent(request.InputStream);
                    httpRequest.Content.Headers.ContentLength = request.ContentLength64;
                    httpRequest.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
                        request.ContentType ?? "application/octet-stream");
                }

                // 异步发送请求（获取响应头后立即开始读取响应体）
                using var httpResponse = await httpClient.SendAsync(httpRequest,
                    HttpCompletionOption.ResponseHeadersRead, ct);

                // 复制响应状态码
                response.StatusCode = (int)httpResponse.StatusCode;
                // 复制响应头（合并多个值为逗号分隔字符串）
                foreach (var header in httpResponse.Headers)
                {
                    response.Headers.Add(header.Key, string.Join(",", header.Value));
                }

                // 处理响应体
                if (httpResponse.Content != null)
                {
                    response.ContentLength64 = httpResponse.Content.Headers.ContentLength.GetValueOrDefault();
                    response.ContentType = httpResponse.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                    await httpResponse.Content.CopyToAsync(response.OutputStream, ct); // 流式复制避免内存占用
                }

                _logger.LogInformation($"HTTP请求 [{requestId}] 完成：{request.HttpMethod} {request.Url} → {httpResponse.StatusCode}，耗时 {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (OperationCanceledException)
            {
                // 处理客户端取消请求（如浏览器关闭）
                response.StatusCode = 503; // Service Unavailable
                _logger.LogWarning($"HTTP请求 [{requestId}] 处理失败：{request.HttpMethod} {request.Url}");
            }
            catch (Exception ex)
            {
                // 统一处理内部错误
                response.StatusCode = 500; // Internal Server Error
                _logger.LogError($"HTTP请求 [{requestId}] 处理失败：{request.HttpMethod} {request.Url}");
            }
            finally
            {
                response.Close(); // 显式关闭响应流（释放资源）
                stopwatch.Stop();
                if (target != null)
                {
                    UpdateResponseTime(target, stopwatch.ElapsedMilliseconds);
                }
            }
        }
        #endregion

        /// <summary>
        /// 停止所有HTTP监听器
        /// 注意：HttpListener.Stop() 会抛出异常给正在等待的GetContextAsync
        /// </summary>
        private async Task StopHttpListenersAsync()
        {
            var tasks = new List<Task>();
            foreach (var listener in _httpListeners.Values)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        listener.Stop(); // 停止监听，终止所有请求处理
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"停止HTTP监听器失败：{ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
            _httpListeners.Clear();
        }
    }
}
