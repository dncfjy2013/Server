using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

enum ProtocolType
{
    Tcp,
    SslTcp,
    Udp,
    Http
}

class AdvancedPortForwarder
{
    private readonly List<EndpointConfig> _endpoints;
    private readonly Dictionary<int, SemaphoreSlim> _portLimiters;
    private readonly HttpClient _httpClient = new();

    #region 配置类
    public class TargetServer
    {
        private int _CurrentConnections;
        public string Ip { get; }
        public int Port { get; }
        public int TargetPort { get; }
        public ProtocolType BackendProtocol { get; set; } = ProtocolType.Tcp;
        public X509Certificate2 ServerCertificate { get; set; }
        public int CurrentConnections => _CurrentConnections;

        public TargetServer(string ip, int port, int targetPort)
        {
            Ip = ip;
            Port = port;
            TargetPort = targetPort;
            _CurrentConnections = 0;
        }

        public void Increment() => Interlocked.Increment(ref _CurrentConnections);
        public void Decrement() => Interlocked.Decrement(ref _CurrentConnections);
    }

    public class EndpointConfig
    {
        public string ListenIp { get; set; } = "0.0.0.0";
        public int ListenPort { get; set; }
        public ProtocolType Protocol { get; set; }
        public List<TargetServer> TargetServers { get; set; } = new();
        public int MaxConnections { get; set; } = 1000;
        public bool ClientCertificateRequired { get; set; }
        public X509Certificate2 ServerCertificate { get; set; }
    }
    #endregion

    #region 构造函数和初始化
    public AdvancedPortForwarder(IEnumerable<EndpointConfig> endpoints)
    {
        _endpoints = new List<EndpointConfig>(endpoints);
        _portLimiters = new Dictionary<int, SemaphoreSlim>();

        foreach (var ep in _endpoints)
        {
            _portLimiters[ep.ListenPort] = new SemaphoreSlim(ep.MaxConnections, ep.MaxConnections);
        }
    }
    #endregion

    #region 主启动方法
    public async Task StartAsync()
    {
        ThreadPool.SetMinThreads(200, 200);

        var tasks = new List<Task>();
        foreach (var ep in _endpoints)
        {
            tasks.Add(Task.Run(async () => await RunEndpointAsync(ep)));
        }

        await Task.WhenAll(tasks);
    }
    #endregion

    #region 端点处理
    private async Task RunEndpointAsync(EndpointConfig ep)
    {
        try
        {
            switch (ep.Protocol)
            {
                case ProtocolType.Tcp:
                case ProtocolType.SslTcp:
                    await RunTcpBasedEndpointAsync(ep);
                    break;
                case ProtocolType.Udp:
                    await RunUdpEndpointAsync(ep);
                    break;
                case ProtocolType.Http:
                    await RunHttpEndpointAsync(ep);
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] 致命错误: {ex.Message}");
        }
    }
    #endregion

    #region TCP协议处理（含SSL）
    private async Task RunTcpBasedEndpointAsync(EndpointConfig ep)
    {
        TcpListener listener = new TcpListener(IPAddress.Parse(ep.ListenIp), ep.ListenPort);
        listener.Start();
        Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] {ep.Protocol} 监听器已启动");

        try
        {
            while (true)
            {
                await _portLimiters[ep.ListenPort].WaitAsync();
                var client = await listener.AcceptTcpClientAsync();
                _ = HandleTcpBasedConnectionAsync(client, ep);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleTcpBasedConnectionAsync(TcpClient client, EndpointConfig ep)
    {
    TargetServer selected = null;
    Stream networkStream = null;
 
    try
    {
        selected = SelectServer(ep.TargetServers);
        selected.Increment();
 
        var targetClient = new TcpClient();
        await targetClient.ConnectAsync(selected.Ip, selected.TargetPort);
 
        if (ep.Protocol == ProtocolType.SslTcp)
        {
            // 修正1：证书集合创建方式
            var certCollection = new X509CertificateCollection(
                new X509Certificate[] { ep.ServerCertificate }
            );
 
            var sslStream = new SslStream(targetClient.GetStream(), false, ValidateClientCertificate);
 
            // 服务器证书验证
            await sslStream.AuthenticateAsClientAsync(
                selected.Ip,
                certCollection,
                SslProtocols.Tls12 | SslProtocols.Tls13,
                false);
 
            // 修正2：客户端证书验证（通过RemoteCertificate属性）
            if (ep.ClientCertificateRequired && sslStream.RemoteCertificate == null)
            {
                throw new AuthenticationException("未提供有效的客户端证书");
            }
 
            networkStream = sslStream;
        }
        else
        {
            networkStream = targetClient.GetStream();
        }
 
        Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] 已连接到 {selected.Ip}:{selected.TargetPort} ({ep.Protocol})");
 
        await ForwardDataAsync(client.GetStream(), networkStream);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] 连接错误: {ex.Message}");
    }
    finally
    {
        selected?.Decrement();
        client?.Dispose();
        networkStream?.Dispose();
        _portLimiters[ep.ListenPort].Release();
    }
}

    private bool ValidateClientCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        Console.WriteLine($"证书警告: {sslPolicyErrors}");
        // 生产环境应实现完整的证书验证逻辑
        return false;
    }
    #endregion

    #region UDP协议处理
    private async Task RunUdpEndpointAsync(EndpointConfig ep)
    {
        using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(ep.ListenIp), ep.ListenPort));
        Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] UDP 监听器已启动");

        try
        {
            while (true)
            {
                var result = await udpClient.ReceiveAsync();
                _ = HandleUdpPacketAsync(result, ep);
            }
        }
        finally
        {
            udpClient.Close();
        }
    }

    private async Task HandleUdpPacketAsync(UdpReceiveResult result, EndpointConfig ep)
    {
        TargetServer selected = null;
        UdpClient targetClient = null;

        try
        {
            selected = SelectServer(ep.TargetServers);
            targetClient = new UdpClient();
            targetClient.Connect(selected.Ip, selected.TargetPort);

            await targetClient.SendAsync(result.Buffer, result.Buffer.Length);
            Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] 已转发UDP数据包到 {selected.Ip}:{selected.TargetPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] UDP 错误: {ex.Message}");
        }
        finally
        {
            targetClient?.Dispose();
        }
    }
    #endregion

    #region HTTP协议处理
    private async Task RunHttpEndpointAsync(EndpointConfig ep)
    {
        if (!HttpListener.IsSupported)
        {
            Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] HTTP监听器不支持");
            return;
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://{ep.ListenIp}:{ep.ListenPort}/");
        listener.Start();
        Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] HTTP 监听器已启动");

        try
        {
            while (true)
            {
                var context = await listener.GetContextAsync();
                _ = HandleHttpRequestAsync(context, ep);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleHttpRequestAsync(HttpListenerContext context, EndpointConfig ep)
    {
        TargetServer selected = null;
        HttpResponseMessage backendResponse = null;

        try
        {
            selected = SelectServer(ep.TargetServers);
            var request = context.Request;

            // 构建目标URI（保持与HttpListener兼容的格式）
            var uriBuilder = new UriBuilder
            {
                Scheme = "http",
                Host = selected.Ip,
                Port = selected.TargetPort,
                Path = request.Url.LocalPath,
                Query = request.Url.Query
            };

            var backendRequest = new HttpRequestMessage(
                new HttpMethod(request.HttpMethod),
                uriBuilder.Uri)
            {
                Content = request.HasEntityBody
                    ? new StreamContent(request.InputStream, (int)request.ContentLength64)
                    : null
            };

            // 优化后的请求头处理（保留必要头信息）
            foreach (string key in request.Headers.AllKeys)
            {
                // 跳过客户端连接相关头
                if (key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 特殊处理Host头（设置为目标服务器地址）
                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    backendRequest.Headers.Host = $"{selected.Ip}:{selected.TargetPort}";
                    continue;
                }

                // 添加其他合法头
                var values = request.Headers.GetValues(key);
                foreach (var value in values)
                {
                    backendRequest.Headers.TryAddWithoutValidation(key, value);
                }
            }

            // 添加客户端真实IP头（可选）
            backendRequest.Headers.Add("X-Forwarded-For", context.Request.RemoteEndPoint.Address.ToString());

            // 发送请求到目标服务器
            backendResponse = await _httpClient.SendAsync(backendRequest, HttpCompletionOption.ResponseHeadersRead);

            // 处理重定向响应（修改Location头）
            if (backendResponse.StatusCode == HttpStatusCode.Redirect ||
                backendResponse.StatusCode == HttpStatusCode.Moved ||
                backendResponse.StatusCode == HttpStatusCode.TemporaryRedirect)
            {
                var newLocation = backendResponse.Headers.Location.ToString()
                    .Replace($"{selected.Ip}:{selected.TargetPort}", context.Request.Url.Authority);

                backendResponse.Headers.Location = new Uri(newLocation);
            }

            // 转发响应状态码
            context.Response.StatusCode = (int)backendResponse.StatusCode;

            // 转发响应头（过滤特殊头）
            foreach (var header in backendResponse.Headers)
            {
                if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;

                context.Response.AddHeader(header.Key, string.Join(",", header.Value));
            }

            // 处理响应内容
            if (backendResponse.Content != null)
            {
                // 保留原始Content-Type
                context.Response.ContentType = backendResponse.Content.Headers.ContentType?.ToString();

                // 流式传输内容（适用于大文件）
                await backendResponse.Content.CopyToAsync(context.Response.OutputStream);
            }
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] 请求失败: {httpEx.StatusCode} - {httpEx.Message}");
            context.Response.StatusCode = 502; // Bad Gateway
            context.Response.OutputStream.Write(Encoding.UTF8.GetBytes("请求处理失败"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now}] [{ep.ListenPort}] 服务器错误: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.OutputStream.Write(Encoding.UTF8.GetBytes("服务器内部错误"));
        }
        finally
        {
            selected?.Decrement();
            backendResponse?.Dispose();
            context.Response.Close();
        }
    }
    #endregion

    #region 通用方法
    private TargetServer SelectServer(List<TargetServer> servers)
    {
        TargetServer selected = null;
        int minConnections = int.MaxValue;

        foreach (var server in servers)
        {
            if (server.CurrentConnections < minConnections)
            {
                minConnections = server.CurrentConnections;
                selected = server;
            }
        }

        return selected ?? throw new InvalidOperationException("无可用服务器");
    }

    private async Task ForwardDataAsync(Stream clientStream, Stream serverStream)
    {
        var pipe = new Pipe();
        var writeTask = FillPipeAsync(clientStream, pipe.Writer);
        var readTask = DrainPipeAsync(serverStream, pipe.Reader);

        await Task.WhenAll(writeTask, readTask);
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
    }

    private async Task FillPipeAsync(Stream source, PipeWriter writer)
    {
        while (true)
        {
            try
            {
                Memory<byte> memory = writer.GetMemory(65536);
                int bytesRead = await source.ReadAsync(memory);

                if (bytesRead == 0) break;

                writer.Advance(bytesRead);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] 读取错误: {ex.Message}");
                break;
            }

            FlushResult result = await writer.FlushAsync();
            if (result.IsCompleted) break;
        }
    }

    private async Task DrainPipeAsync(Stream destination, PipeReader reader)
    {
        while (true)
        {
            try
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                if (buffer.IsEmpty && !result.IsCompleted) continue;

                // 初始化消费位置
                long consumed = 0;

                foreach (var memory in buffer)
                {
                    // 将每个内存段写入流
                    await destination.WriteAsync(memory);

                    // 累加已消费的字节数
                    consumed += memory.Length;
                }

                // 推进读取位置到已消费的位置
                reader.AdvanceTo(buffer.GetPosition(consumed), buffer.End);

                if (result.IsCompleted) break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now}] 写入错误: {ex.Message}");
                break;
            }
        }

        // 完成读取操作
        await reader.CompleteAsync();
    }
    #endregion
}
