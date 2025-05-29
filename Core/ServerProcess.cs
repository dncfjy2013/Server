using Core.Message;
using Protocol;
using Server.Core.Common;
using Server.Core.Config;
using Server.Utils;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace Server.Core
{
    partial class ServerInstance
    {

        // 一个布尔类型的标志，用于控制是否继续接收新的数据
        // 当设置为 true 时，服务器会继续接收新的客户端消息；设置为 false 时，停止接收新消息
        private bool _isReceiving = true;

        // 一个布尔类型的标志，用于控制是否允许实时数据功能
        private bool _isRealTimeTransferAllowed = false;

        
        /// <summary>
        /// 处理客户端连接（生产者逻辑：接收消息并按优先级入队）
        /// </summary>
        /// <param name="client">客户端配置对象</param>
        private async Task HandleClient(ClientConfig client)
        {
            _logger.LogTrace($"Client {client.Id} connection started (RemoteEndPoint={client.Socket?.RemoteEndPoint})");

            try
            {
                // 初始化数据流（普通Socket或SSL流）
                Stream stream = client.Socket != null
                    ? new NetworkStream(client.Socket)
                    : client.SslStream;
                _logger.LogDebug($"Client {client.Id} using {stream.GetType().Name} for communication");

                while (_isRunning && _isReceiving)
                {
                    try
                    {
                        // 1. 接收消息头部（固定8字节）
                        byte[] headerBuffer = new byte[8];
                        _logger.LogTrace($"Client {client.Id} reading header (8 bytes)");
                        if (!await ReadFullAsync(stream, headerBuffer, 8))
                        {
                            _logger.LogWarning($"Client {client.Id} disconnected while reading header");
                            return;
                        }
                        _logger.LogDebug($"Client {client.Id} header received successfully");

                        // 2. 解析协议头部
                        _logger.LogTrace($"Client {client.Id} parsing header bytes");
                        if (!ProtocolHeaderExtensions.TryFromBytes(headerBuffer, out var header))
                        {
                            _logger.LogWarning($"Client {client.Id} invalid header format: {BitConverter.ToString(headerBuffer)}");
                            continue;
                        }
                        _logger.LogDebug($"Client {client.Id} header parsed: Version={header.Version}, Length={header.MessageLength}");

                        // 3. 校验协议版本
                        if (!ConstantsConfig.config.SupportedVersions.Contains((byte)header.Version))
                        {
                            _logger.LogWarning($"Client {client.Id} unsupported version {header.Version} (supported: {string.Join(",", ConstantsConfig.config.SupportedVersions)})");
                            continue;
                        }
                        _logger.LogDebug($"Client {client.Id} protocol version {header.Version} verified");

                        // 4. 接收消息体
                        byte[] payloadBuffer = new byte[header.MessageLength];
                        _logger.LogTrace($"Client {client.Id} reading payload ({header.MessageLength} bytes)");
                        if (!await ReadFullAsync(stream, payloadBuffer, (int)header.MessageLength))
                        {
                            _logger.LogWarning($"Client {client.Id} disconnected while reading payload");
                            return;
                        }
                        _logger.LogDebug($"Client {client.Id} payload received ({header.MessageLength} bytes)");

                        // 5. 组装完整数据包
                        byte[] fullPacket = new byte[8 + header.MessageLength];
                        Buffer.BlockCopy(headerBuffer, 0, fullPacket, 0, 8);
                        Buffer.BlockCopy(payloadBuffer, 0, fullPacket, 8, (int)header.MessageLength);
                        _logger.LogTrace($"Client {client.Id} assembled full packet (size={fullPacket.Length} bytes)");

                        // 6. 解析数据包
                        _logger.LogTrace($"Client {client.Id} trying to parse packet");
                        var (success, packet, error) = ProtocolPacketWrapper.TryFromBytes(fullPacket);
                        if (!success)
                        {
                            _logger.LogWarning($"Client {client.Id} packet parsing failed: {error}");
                            continue;
                        }
                        _logger.LogDebug($"Client {client.Id} packet parsed successfully (Priority={packet.Data.Priority})");

                        // 更新客户端活动时间
                        client.UpdateActivity();
                        client.SetValue(packet.Data.Sourceid);
                        _logger.LogTrace($"Client {client.Id} activity time updated");

                        // 判断是否为视频或语音通信请求
                        if (IsVideoOrVoiceRequest(packet.Data))
                        {
                            // 查找目标客户端
                            var targetClient = _clients.Values.FirstOrDefault(c => c.UniqueId == packet.Data.Targetid);
                            if (targetClient != null) 
                            {
                                // 添加监测功能
                                if (!_isRealTimeTransferAllowed)
                                {
                                    _logger.LogDebug("Data RealTime transfer is paused");
                                    await _messageManager.SendInfoDate(targetClient, packet.Data);
                                    continue;
                                }
                                else
                                {
                                    // 建立直接连接
                                    await EstablishDirectConnection(client, targetClient);
                                }
                                continue;
                            }
                            else
                            {
                                _logger.LogWarning($"Client {client.Id} target client {packet.Data.Targetid} not found");
                                continue;
                            }
                        }

                        // 创建消息对象
                        var message = new ClientMessage
                        {
                            Client = client,
                            Data = packet.Data,
                            ReceivedTime = DateTime.Now
                        };

                        if (ConstantsConfig.IsUnityServer)
                        {

                            // 背压策略：丢弃低优先级消息（队列积压时）
                            bool isQueueFull = _messageManager._messageHighQueue.Reader.Count > ConstantsConfig.MaxQueueSize ||
                                               _messageManager._messageMediumQueue.Reader.Count > ConstantsConfig.MaxQueueSize ||
                                               _messageManager._messagelowQueue.Reader.Count > ConstantsConfig.MaxQueueSize;

                            if (isQueueFull && message.Data.Priority == DataPriority.Low)
                            {
                                _logger.LogCritical($"Client {client.Id} discarded low-priority message (queue full: High={_messageManager._messageHighQueue.Reader.Count}, Medium={_messageManager._messageMediumQueue.Reader.Count}, Low={_messageManager._messagelowQueue.Reader.Count})");
                                continue;
                            }

                            // 按优先级入队
                            switch (packet.Data.Priority)
                            {
                                case DataPriority.Low:
                                    await _messageManager._messagelowQueue.Writer.WriteAsync(message);
                                    _logger.LogDebug($"Client {client.Id} low-priority message enqueued (Id={message.Client.Id})");
                                    break;
                                case DataPriority.High:
                                    await _messageManager._messageHighQueue.Writer.WriteAsync(message);
                                    _logger.LogDebug($"Client {client.Id} high-priority message enqueued (Id={message.Client.Id})");
                                    break;
                                case DataPriority.Medium:
                                    await _messageManager._messageMediumQueue.Writer.WriteAsync(message);
                                    _logger.LogDebug($"Client {client.Id} medium-priority message enqueued (Id={message.Client.Id})");
                                    break;
                            }

                            // 队列积压监控与背压（按优先级分级处理）
                            await MonitorQueueBackpressure(client, packet.Data.Priority, (int)header.MessageLength);
                        }
                        else
                        {
                            _messageManager.ProcessMessageWithPriority(message, message.Data.Priority);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogCritical($"Client {client.Id} unexpected error: {ex.Message}  ");
                        break; // 终止当前客户端处理循环
                    }
                }
            }
            finally
            {
                DisconnectClient(client.Id);
                _logger.LogInformation($"Client {client.Id} connection terminated");
            }
        }

        /// <summary>
        /// 监控队列积压并执行背压策略
        /// </summary>
        private async Task MonitorQueueBackpressure(ClientConfig client, DataPriority priority, int messageSize)
        {
            switch (priority)
            {
                case DataPriority.Low:
                    if (_messageManager._messagelowQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} LOW QUEUE BACKPRESSURE: {_messageManager._messagelowQueue.Reader.Count} messages积压");
                        await ImplementBackpressure(client, TimeSpan.FromSeconds(1)); // 低优先级暂停1秒
                    }
                    break;
                case DataPriority.Medium:
                    if (_messageManager._messageMediumQueue.Reader.Count > ConstantsConfig.MaxQueueSize * 0.9) // 90%阈值预警
                    {
                        _logger.LogWarning($"Client {client.Id} MEDIUM QUEUE NEAR BACKPRESSURE: {_messageManager._messageMediumQueue.Reader.Count}/{ConstantsConfig.MaxQueueSize}");
                    }
                    if (_messageManager._messageMediumQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} MEDIUM QUEUE BACKPRESSURE: {_messageManager._messageMediumQueue.Reader.Count} messages积压");
                        await ImplementBackpressure(client, TimeSpan.FromMilliseconds(600)); // 中等优先级暂停600ms
                    }
                    break;
                case DataPriority.High:
                    if (_messageManager._messageHighQueue.Reader.Count > ConstantsConfig.MaxQueueSize * 0.9) // 90%阈值预警
                    {
                        _logger.LogWarning($"Client {client.Id} HIGH QUEUE NEAR BACKPRESSURE: {_messageManager._messageHighQueue.Reader.Count}/{ConstantsConfig.MaxQueueSize}");
                    }
                    if (_messageManager._messageHighQueue.Reader.Count > ConstantsConfig.MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} HIGH QUEUE BACKPRESSURE: {_messageManager._messageHighQueue.Reader.Count} messages积压");
                        await ImplementBackpressure(client, TimeSpan.FromMilliseconds(200)); // 高优先级暂停200ms
                    }
                    break;
            }
        }

        /// <summary>
        /// 执行背压策略（暂停接收新消息）
        /// </summary>
        private async Task ImplementBackpressure(ClientConfig client, TimeSpan delay)
        {
            _logger.LogCritical($"Client {client.Id} applying backpressure: pausing receive for {delay.TotalMilliseconds}ms");
            _isReceiving = false; // 暂停接收新消息
            await Task.Delay(delay);
            _isReceiving = true;
            _logger.LogCritical($"Client {client.Id} backpressure released: resume receiving");
        }

        // 判断是否为视频或语音通信请求
        private bool IsVideoOrVoiceRequest(CommunicationData data)
        {
            // 这里需要根据实际的协议定义来判断
            // 假设存在一个字段来标识视频或语音通信请求
            return data.InfoType == InfoType.CtcVideo || data.InfoType == InfoType.CtcVoice;
        }

        // 建立直接连接
        private async Task EstablishDirectConnection(ClientConfig client1, ClientConfig client2)
        {
            _logger.LogTrace($"Starting to establish direct connection between client {client1.UniqueId} and client {client2.UniqueId}.");

            try
            {
                _logger.LogDebug($"Opening network streams for client {client1.UniqueId} and client {client2.UniqueId }.");
                using var stream1 = new NetworkStream(client1.Socket);
                using var stream2 = new NetworkStream(client2.Socket);

                _logger.LogInformation($"Successfully opened network streams for client {client1.UniqueId} and client {client2.UniqueId}. Establishing bidirectional data transfer.");

                var task1 = CopyStreamAsync(stream1, stream2);
                var task2 = CopyStreamAsync(stream2, stream1);

                await Task.WhenAll(task1, task2);

                _logger.LogInformation($"Direct connection between client {client1.UniqueId} and client {client2.UniqueId} has been successfully established and data transfer completed.");
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning($"One of the sockets for client {client1.UniqueId} or client {client2.UniqueId} is disposed. Error: {ex.Message}");
            }
            catch (IOException ex)
            {
                _logger.LogError($"An I/O error occurred while establishing direct connection between client {client1.UniqueId} and client {client2.UniqueId}. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Unexpected error while establishing direct connection between client {client1.UniqueId} and client {client2.UniqueId}. Error: {ex.Message}");
            }
            finally
            {
                _logger.LogTrace($"Ending the process of establishing direct connection between client {client1.UniqueId} and client {client2.UniqueId}.");
            }
        }

        // 异步复制流
        private async Task CopyStreamAsync(Stream source, Stream destination)
        {
            _logger.LogTrace($"Starting to copy data from source stream to destination stream.");
            try
            {
                byte[] buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    _logger.LogDebug($"Read {bytesRead} bytes from source stream. Writing to destination stream.");
                    await destination.WriteAsync(buffer, 0, bytesRead);
                    _logger.LogDebug($"Successfully wrote {bytesRead} bytes to destination stream.");
                }
                _logger.LogInformation($"Data copying from source stream to destination stream completed.");
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning($"One of the streams is disposed during data copying. Error: {ex.Message}");
            }
            catch (IOException ex)
            {
                _logger.LogError($"An I/O error occurred during data copying. Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Unexpected error during data copying. Error: {ex.Message}");
            }
            finally
            {
                _logger.LogTrace($"Ending the data copying process from source stream to destination stream.");
            }
        }

        public void ModifyRealTimeTransfer(bool value)
        {
            _isRealTimeTransferAllowed = value;
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

        private async Task HandleUdpData(IPEndPoint remoteEndPoint, byte[] data)
        {
            try
            {
                // 这里可以实现具体的 UDP 数据处理逻辑
                string message = System.Text.Encoding.UTF8.GetString(data);
                _logger.LogInformation($"Received UDP message from {remoteEndPoint}: {message}");

                // 示例：回显消息给客户端
                await _udpListener.SendAsync(data, data.Length, remoteEndPoint);
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Error handling UDP data: {ex.Message}, {ex}");
            }
        }
    }
}
