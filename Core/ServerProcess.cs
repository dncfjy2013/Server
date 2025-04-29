using Protocol;
using Server.Client;
using Server.Common;
using Server.Extend;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Server.Core
{
    partial class ServerInstance
    {
        // 定义消息队列的最大容量，这里设置为 int 类型的最大值，表示队列理论上可以无限扩展
        private readonly int MaxQueueSize = int.MaxValue;

        // 创建一个无界的通道用于存储高优先级的客户端消息
        // 通道是一种用于在不同线程或任务之间安全传递数据的机制
        private Channel<ClientMessage> _messageHighQueue = Channel.CreateUnbounded<ClientMessage>();

        // 创建一个无界的通道用于存储中优先级的客户端消息
        private Channel<ClientMessage> _messageMediumQueue = Channel.CreateUnbounded<ClientMessage>();

        // 创建一个无界的通道用于存储低优先级的客户端消息
        private Channel<ClientMessage> _messagelowQueue = Channel.CreateUnbounded<ClientMessage>();

        /// <summary>
        /// 高优先级消息的线程管理器实例。负责管理和处理高优先级的客户端消息队列。
        /// 该管理器会根据系统的 CPU 核心数动态调整线程数量，以确保高优先级消息能够被快速处理。
        /// 其最小线程数设置为当前系统的 CPU 核心数，最大线程数为 CPU 核心数的两倍。
        /// </summary>
        private IncomingMessageThreadManager _incomingHighManager;

        /// <summary>
        /// 中优先级消息的线程管理器实例。用于管理和处理中优先级的客户端消息队列。
        /// 为了平衡系统资源分配，该管理器启动的线程数量为 CPU 核心数的一半，
        /// 最小线程数是 CPU 核心数的一半，最大线程数为 CPU 核心数。
        /// </summary>
        private IncomingMessageThreadManager _incomingMediumManager;

        /// <summary>
        /// 低优先级消息的线程管理器实例。主要负责处理低优先级的客户端消息队列。
        /// 由于低优先级消息对处理及时性要求较低，所以该管理器只启动较少的线程，
        /// 最小线程数为 1，最大线程数为 2。
        /// </summary>
        private IncomingMessageThreadManager _incomingLowManager;

        // 定义一个字典，用于存储不同优先级对应的信号量
        // 信号量用于控制并发访问的数量，确保系统资源不会被过度占用
        private readonly Dictionary<DataPriority, SemaphoreSlim> _prioritySemaphores = new()
        {
            // 为高优先级消息设置信号量，允许的并发数量为处理器核心数的两倍
            [DataPriority.High] = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2),
            // 为中优先级消息设置信号量，允许的并发数量等于处理器核心数
            [DataPriority.Medium] = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount),
            // 为低优先级消息设置信号量，允许的并发数量为处理器核心数的一半
            [DataPriority.Low] = new SemaphoreSlim(Environment.ProcessorCount / 2, Environment.ProcessorCount / 2)
        };

        // 创建一个用于取消消息处理任务的 CancellationTokenSource
        // 当需要停止消息处理时，可以调用该对象的 Cancel 方法来取消相关任务
        private readonly CancellationTokenSource _processingCts = new();

        // 一个布尔类型的标志，用于控制是否继续接收新的数据
        // 当设置为 true 时，服务器会继续接收新的客户端消息；设置为 false 时，停止接收新消息
        private bool _isReceiving = true;

        // 一个布尔类型的标志，用于控制是否允许实时数据功能
        private bool _isRealTimeTransferAllowed = false;

        /// <summary>
        /// 启动消息处理消费者。此方法应在服务启动时调用，根据 CPU 核心数为不同优先级的消息队列启动相应数量的消费者任务。
        /// </summary>
        public void StartProcessing()
        {
            try
            {
                _logger.LogTrace("Entering StartProcessing method.");
                _logger.LogDebug("Initiating the initialization of message thread managers for different priorities.");

                // 获取当前系统的 CPU 核心数，用于动态确定不同优先级消息队列的消费者数量
                int processorCount = Environment.ProcessorCount;
                _logger.LogDebug($"Current system CPU core count obtained: {processorCount}."); // 改为Debug级别（Critical通常用于致命错误）

                // 启动高优先级消息的处理任务
                _logger.LogInformation("Starting the initialization of the high-priority message thread manager.");
                _incomingHighManager = new IncomingMessageThreadManager(
                    this,
                    _messageHighQueue,
                    _logger,
                    DataPriority.High,
                    minThreads: processorCount / 2,
                    maxThreads: processorCount * 2);
                _logger.LogInformation("High-priority message thread manager initialization completed.");

                // 启动中优先级消息的处理任务
                _logger.LogInformation("Starting the initialization of the medium-priority message thread manager.");
                _incomingMediumManager = new IncomingMessageThreadManager(
                    this,
                    _messageMediumQueue,
                    _logger,
                    DataPriority.Medium,
                    minThreads: processorCount / 4,
                    maxThreads: processorCount);
                _logger.LogInformation("Medium-priority message thread manager initialization completed.");

                // 启动低优先级消息的处理任务
                _logger.LogInformation("Starting the initialization of the low-priority message thread manager.");
                _incomingLowManager = new IncomingMessageThreadManager(
                    this,
                    _messagelowQueue,
                    _logger,
                    DataPriority.Low,
                    minThreads: 1,
                    maxThreads: processorCount / 4);
                _logger.LogInformation("Low-priority message thread manager initialization completed.");

                _logger.LogDebug("Initialization of all priority message thread managers completed.");
                _logger.LogTrace("Exiting StartProcessing method.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while starting message processing consumers: {ex.Message}");
                _logger.LogWarning("Due to the error, some or all message thread managers may not have been initialized successfully.");
            }
        }

        /// <summary>
        /// 处理不同优先级的客户端消息（消费者核心逻辑）
        /// </summary>
        /// <param name="priority">消息优先级</param>
        public async Task ProcessMessages(DataPriority priority)
        {
            // 获取对应优先级的信号量（控制并发处理数量）
            var semaphore = _prioritySemaphores[priority];
            _logger.LogDebug($"Acquired semaphore for {priority} priority (CurrentCount={semaphore.CurrentCount})");

            try
            {
                switch (priority)
                {
                    case DataPriority.High:
                        _logger.LogInformation($"High priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
                        await ProcessPriorityMessages(priority, _messageHighQueue.Reader, semaphore);
                        break;
                    case DataPriority.Medium:
                        _logger.LogInformation($"Medium priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
                        await ProcessPriorityMessages(priority, _messageMediumQueue.Reader, semaphore);
                        break;
                    case DataPriority.Low:
                        _logger.LogInformation($"Low priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
                        await ProcessPriorityMessages(priority, _messagelowQueue.Reader, semaphore);
                        break;
                    default:
                        _logger.LogWarning($"Unknown priority {priority} received, skipping processor");
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug($"{priority} priority processor was canceled");
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Unhandled exception in {priority} processor: {ex.Message}  ");
            }
            finally
            {
                _logger.LogInformation($"{priority} priority processor stopped");
            }
        }

        /// <summary>
        /// 处理特定优先级消息的通用逻辑
        /// </summary>
        /// <param name="priority">消息优先级</param>
        /// <param name="reader">通道读取器</param>
        private async Task ProcessPriorityMessages(DataPriority priority, ChannelReader<ClientMessage> reader, SemaphoreSlim semaphore)
        {
            // 异步遍历通道中的所有消息（支持取消令牌）
            await foreach (var message in reader.ReadAllAsync(_processingCts.Token))
            {
                // 验证消息优先级与处理器匹配（防御性编程）
                if (message.Data.Priority != priority)
                {
                    _logger.LogTrace($"Dropping message with mismatched priority: expected {priority}, actual {message.Data.Priority}");
                    continue;
                }

                _logger.LogDebug($"Received {priority} priority message: Id={message.Client.Id}, Size={MemoryCalculator.CalculateObjectSize(message.Data)} bytes");

                // 等待信号量（控制并发数）
                await semaphore.WaitAsync(_processingCts.Token);
                _logger.LogTrace($"{priority} semaphore acquired (current count={semaphore.CurrentCount})");

                try
                {
                    // 处理消息核心逻辑（假设包含业务处理和耗时操作）
                    await ProcessMessageWithPriority(message, priority);
                    _logger.LogDebug($"{priority} message processed successfully: Id={message.Client.Id}");
                }
                catch (TimeoutException tex)
                {
                    _logger.LogError($"Timeout processing {priority} message {message.Client.Id}: {tex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error processing {priority} message {message.Client.Id}: {ex.Message}  ");
                    if (priority == DataPriority.High)
                    {
                        _logger.LogWarning($"High-priority message failed: {message.Client.Id}, will retry later");
                    }
                }
                finally
                {
                    semaphore.Release();
                    _logger.LogTrace($"{priority} semaphore released (current count={semaphore.CurrentCount})");
                }
            }
        }

        /// <summary>
        /// 按优先级处理客户端消息（核心业务逻辑）
        /// </summary>
        /// <param name="message">待处理的客户端消息</param>
        /// <param name="priority">消息优先级</param>
        private async Task ProcessMessageWithPriority(ClientMessage message, DataPriority priority)
        {
            // 根据优先级设置不同的处理超时时间
            var timeout = priority switch
            {
                DataPriority.High => TimeSpan.FromMilliseconds(100),  // 高优先级消息要求快速响应
                DataPriority.Medium => TimeSpan.FromMilliseconds(500), // 中等优先级允许稍长处理时间
                _ => TimeSpan.FromSeconds(1)                           // 低优先级默认1秒超时
            };
            _logger.LogDebug($"Set {priority} priority message timeout to {timeout.TotalMilliseconds}ms");

            // 使用CancellationTokenSource实现处理超时控制
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                _logger.LogTrace($"Start processing {priority} message (Id={message.Client.Id}, Type={message.Data.InfoType})");

                // 根据消息类型分发不同的处理逻辑
                switch (message.Data.InfoType)
                {
                    case InfoType.HeartBeat:
                        _logger.LogDebug($"Handling heartbeat from client {message.Client.Id}");
                        await HandleHeartbeat(message.Client, message.Data);
                        _logger.LogDebug($"Heartbeat handled successfully for client {message.Client.Id}");
                        break;

                    case InfoType.CtsFile:
                        _logger.LogDebug($"Handling file transfer for client {message.Client.Id} (Size={MemoryCalculator.CalculateObjectSize(message.Data)} bytes)");
                        await HandleFileTransfer(message.Client, message.Data);
                        _logger.LogDebug($"File transfer completed for client {message.Client.Id}");
                        break;

                    case InfoType.CtsNormal:
                        _logger.LogDebug($"Handling normal message for client {message.Client.Id} (Content={message.Data.Message})");
                        await HandleNormalMessage(message.Client, message.Data);
                        _logger.LogDebug($"Normal message processed for client {message.Client.Id}");
                        break;
                }

                _logger.LogTrace($"Completed processing {priority} message (Id={message.Client.Id})");
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // 处理超时异常（优先级相关）
                _logger.LogError($"{priority} priority message processing timed out (Id={message.Client.Id})");
                _logger.LogWarning($"Client {message.Client.Id} may experience delay due to timeout");
            }
            catch (Exception ex)
            {
                // 处理其他异常（非超时原因）
                _logger.LogError($"Unhandled error processing {priority} message (Id={message.Client.Id}): {ex.Message}  ");
                if (priority == DataPriority.High)
                {
                    _logger.LogCritical($"High-priority message failure requires immediate attention: {message.Client.Id}");
                }
            }
        }

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
                        if (!config.SupportedVersions.Contains((byte)header.Version))
                        {
                            _logger.LogWarning($"Client {client.Id} unsupported version {header.Version} (supported: {string.Join(",", config.SupportedVersions)})");
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
                                    await SendDate(targetClient, packet.Data);
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

                        // 背压策略：丢弃低优先级消息（队列积压时）
                        bool isQueueFull = _messageHighQueue.Reader.Count > MaxQueueSize ||
                                           _messageMediumQueue.Reader.Count > MaxQueueSize ||
                                           _messagelowQueue.Reader.Count > MaxQueueSize;

                        if (isQueueFull && message.Data.Priority == DataPriority.Low)
                        {
                            _logger.LogCritical($"Client {client.Id} discarded low-priority message (queue full: High={_messageHighQueue.Reader.Count}, Medium={_messageMediumQueue.Reader.Count}, Low={_messagelowQueue.Reader.Count})");
                            continue;
                        }

                        // 按优先级入队
                        switch (packet.Data.Priority)
                        {
                            case DataPriority.Low:
                                await _messagelowQueue.Writer.WriteAsync(message);
                                _logger.LogDebug($"Client {client.Id} low-priority message enqueued (Id={message.Client.Id})");
                                break;
                            case DataPriority.High:
                                await _messageHighQueue.Writer.WriteAsync(message);
                                _logger.LogDebug($"Client {client.Id} high-priority message enqueued (Id={message.Client.Id})");
                                break;
                            case DataPriority.Medium:
                                await _messageMediumQueue.Writer.WriteAsync(message);
                                _logger.LogDebug($"Client {client.Id} medium-priority message enqueued (Id={message.Client.Id})");
                                break;
                        }

                        // 队列积压监控与背压（按优先级分级处理）
                        await MonitorQueueBackpressure(client, packet.Data.Priority, (int)header.MessageLength);
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
                    if (_messagelowQueue.Reader.Count > MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} LOW QUEUE BACKPRESSURE: {_messagelowQueue.Reader.Count} messages积压");
                        await ImplementBackpressure(client, TimeSpan.FromSeconds(1)); // 低优先级暂停1秒
                    }
                    break;
                case DataPriority.Medium:
                    if (_messageMediumQueue.Reader.Count > MaxQueueSize * 0.9) // 90%阈值预警
                    {
                        _logger.LogWarning($"Client {client.Id} MEDIUM QUEUE NEAR BACKPRESSURE: {_messageMediumQueue.Reader.Count}/{MaxQueueSize}");
                    }
                    if (_messageMediumQueue.Reader.Count > MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} MEDIUM QUEUE BACKPRESSURE: {_messageMediumQueue.Reader.Count} messages积压");
                        await ImplementBackpressure(client, TimeSpan.FromMilliseconds(600)); // 中等优先级暂停600ms
                    }
                    break;
                case DataPriority.High:
                    if (_messageHighQueue.Reader.Count > MaxQueueSize * 0.9) // 90%阈值预警
                    {
                        _logger.LogWarning($"Client {client.Id} HIGH QUEUE NEAR BACKPRESSURE: {_messageHighQueue.Reader.Count}/{MaxQueueSize}");
                    }
                    if (_messageHighQueue.Reader.Count > MaxQueueSize)
                    {
                        _logger.LogCritical($"Client {client.Id} HIGH QUEUE BACKPRESSURE: {_messageHighQueue.Reader.Count} messages积压");
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

            _logger.LogTrace($"Handling HTTP client with request: {context.Request.Url}");

            try
            {
                using (var response = context.Response)
                {
                    // 设置响应的内容类型
                    response.ContentType = "text/html; charset=utf-8";

                    // 读取请求内容
                    string requestContent = await ReadRequestContentAsync(context.Request);
                    _logger.LogDebug($"Received request content: {requestContent}");

                    // 根据请求内容生成响应
                    string responseString = GenerateResponse(requestContent);
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

        private async Task<string> ReadRequestContentAsync(HttpListenerRequest request)
        {
            using (var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private string GenerateResponse(string requestContent)
        {
            // 这里可以根据请求内容生成不同的响应
            // 目前简单返回固定的欢迎信息
            return "<html><body><h1>Hello, HTTP Client!</h1></body></html>";
        }
    }
}
