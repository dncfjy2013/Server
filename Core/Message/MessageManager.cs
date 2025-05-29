using Google.Protobuf;
using MySqlX.XDevAPI;
using Protocol;
using Server.Core;
using Server.Core.Common;
using Server.Core.Config;
using Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Core.Message
{
    // 定义主动消息结构体
    public class ServerOutgoingMessage
    {
        public CommunicationData Data { get; set; }
        public int RetryCount { get; set; } = 0; // 重传次数
        public DateTime SentTime { get; set; } // 发送时间
        public DataPriority Priority { get; set; } // 优先级
    }
    public partial class MessageManager
    {

        private readonly ConcurrentDictionary<uint, ClientConfig> _clients;

        public Channel<ClientMessage> _messageHighQueue = Channel.CreateUnbounded<ClientMessage>();
        public Channel<ClientMessage> _messageMediumQueue = Channel.CreateUnbounded<ClientMessage>();
        public Channel<ClientMessage> _messagelowQueue = Channel.CreateUnbounded<ClientMessage>();

        private IncomingMessageThreadManager _inComingHighManager;
        private IncomingMessageThreadManager _inComingMediumManager;
        private IncomingMessageThreadManager _inComingLowManager;
        private OutgoingMessageThreadManager _outComingHighManager;
        private OutgoingMessageThreadManager _outComingMediumManager;
        private OutgoingMessageThreadManager _outComingLowManager;

        // 信号量用于控制并发访问的数量，确保系统资源不会被过度占用
        private readonly Dictionary<DataPriority, SemaphoreSlim> _prioritySemaphores = new()
        {
            [DataPriority.High] = new SemaphoreSlim(ConstantsConfig.High_Min_Semaphores, ConstantsConfig.High_Max_Semaphores),
            [DataPriority.Medium] = new SemaphoreSlim(ConstantsConfig.Medium_Min_Semaphores, ConstantsConfig.Medium_Max_Semaphores),
            [DataPriority.Low] = new SemaphoreSlim(ConstantsConfig.Low_Min_Semaphores, ConstantsConfig.Low_Max_Semaphores)
        };

        // 当需要停止消息处理时，可以调用该对象的 Cancel 方法来取消相关任务
        private readonly CancellationTokenSource _cts = new();

        private ILogger _logger;

        private readonly Channel<ServerOutgoingMessage> _outgoingHighMessages =
            Channel.CreateUnbounded<ServerOutgoingMessage>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly Channel<ServerOutgoingMessage> _outgoingMedumMessages =
            Channel.CreateUnbounded<ServerOutgoingMessage>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly Channel<ServerOutgoingMessage> _outgoingLowMessages =
            Channel.CreateUnbounded<ServerOutgoingMessage>(new UnboundedChannelOptions { SingleWriter = true });

        private readonly Dictionary<DataPriority, (int MaxRetries, TimeSpan Interval)> _retryPolicies = new() {
                { DataPriority.High, (MaxRetries: ConstantsConfig.Send_High_Retry, Interval: ConstantsConfig.Send_High_Retry_TimeSpan) }, // 高优先级：3次重试，间隔10秒
                { DataPriority.Medium, (MaxRetries: ConstantsConfig.Send_Medium_Retry, Interval: ConstantsConfig.Send_Medium_Retry_TimeSpan) }, // 中优先级：2次重试，间隔20秒
                { DataPriority.Low, (MaxRetries: ConstantsConfig.Send_Low_Retry, Interval: ConstantsConfig.Send_Low_Retry_TimeSpan) } }; // 低优先级：1次重试，间隔30秒

        public MessageManager(ConcurrentDictionary<uint, ClientConfig> clients, ILogger logger)
        {
            _clients = clients;
            _logger = logger;
        }

        public void StartOutgoingMessageProcessing()
        {
            int Threads = Environment.ProcessorCount;
            _logger.LogDebug("Send Process Start High Thread Manager");
            _outComingHighManager = new OutgoingMessageThreadManager(
                this,
                _outgoingHighMessages,
                _logger,
                DataPriority.High,
                minThreads: ConstantsConfig.Out_High_MinThreadNum,
                maxThreads: ConstantsConfig.Out_High_MaxThreadNum);
            _logger.LogDebug("Send Process Start Medium Thread Manager");
            _outComingMediumManager = new OutgoingMessageThreadManager(
                this,
                _outgoingMedumMessages,
                _logger,
                DataPriority.Medium,
                minThreads: ConstantsConfig.Out_Medium_MinThreadNum,
                maxThreads: ConstantsConfig.Out_Medium_MaxThreadNum);
            _logger.LogDebug("Send Process Start Low Thread Manager");
            _outComingLowManager = new OutgoingMessageThreadManager(
                this,
                _outgoingLowMessages,
                _logger,
                DataPriority.Low,
                minThreads: ConstantsConfig.Out_Low_MinThreadNum,
                maxThreads: ConstantsConfig.Out_Low_MaxThreadNum);
        }

        public async Task ProcessOutgoingMessages(ServerOutgoingMessage msg, CancellationToken ct)
        {
            try
            {
                var client = _clients.Values.FirstOrDefault(c => c.UniqueId == msg.Data.Targetid);
                if (client == null)
                {
                    await HandleRetry(msg);
                    _logger.LogWarning($"Could not find client with UniqueId {msg.Data.Targetid} for message.");
                    return;
                }

                // 发送消息
                bool sent = await SendInfoDate(client, msg.Data);
                if (!sent) throw new Exception("Send failed");

                msg.SentTime = DateTime.Now;
                _logger.LogInformation($"Sent {msg.Data.InfoType} to {client.Id} (Priority: {msg.Data.Priority})");

                // 记录发送成功，无需重传
                if (msg.Data.InfoType == InfoType.StcFile && msg.Data.Message == "FILE_COMPLETE")
                {
                    // 文件完成消息无需重传
                    return;
                }

                msg.RetryCount++;

                await HandleRetry(msg);

                // 检查取消令牌状态
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"Message processing for client {msg.Data.Targetid} was cancelled.");
                await HandleRetry(msg);
                return;
            }
            catch (Exception ex)
            {
                // 触发重传逻辑
                _logger.LogWarning($"Retrying message to {msg.Data.Targetid} Because Send failed");
                await HandleRetry(msg);
            }

        }

        private async Task HandleRetry(ServerOutgoingMessage msg)
        {
            var policy = _retryPolicies[msg.Priority];
            if (msg.RetryCount >= policy.MaxRetries)
            {
                _logger.LogWarning($"Max retries exceeded for message to {msg.Data.Targetid}. Dropping.");

                var queue = _ResumeMessages.GetOrAdd(msg.Data.Targetid, new ConcurrentQueue<CommunicationData>());
                queue.Enqueue(msg.Data);
                return;
            }

            await Task.Delay(policy.Interval, _cts.Token); // 按优先级等待间隔

            // 根据优先级重新入队
            switch (msg.Priority)
            {
                case DataPriority.High:
                    _outgoingHighMessages.Writer.TryWrite(msg);
                    break;
                case DataPriority.Medium:
                    _outgoingMedumMessages.Writer.TryWrite(msg);
                    break;
                case DataPriority.Low:
                    _outgoingLowMessages.Writer.TryWrite(msg);
                    break;
            }
            _logger.LogInformation($"Retrying message to {msg.Data.Targetid}");
        }

        public void SendToClient(uint clientId, CommunicationData data, DataPriority priority = DataPriority.Medium)
        {
            if (!_clients.TryGetValue(clientId, out var client)) return;
            switch (priority)
            {
                case DataPriority.High:
                    _outgoingHighMessages.Writer.TryWrite(new ServerOutgoingMessage
                    {
                        Data = data,
                        Priority = priority,
                        RetryCount = -1
                    });
                    break;
                case DataPriority.Medium:
                    _outgoingMedumMessages.Writer.TryWrite(new ServerOutgoingMessage
                    {
                        Data = data,
                        Priority = priority,
                        RetryCount = -1
                    });
                    break;
                case DataPriority.Low:
                    _outgoingLowMessages.Writer.TryWrite(new ServerOutgoingMessage
                    {
                        Data = data,
                        Priority = priority,
                        RetryCount = -1
                    });
                    break;
            }

        }
        public void ShutDown()
        {
            // 取消消息处理取消令牌源，同样是关键操作步骤，使用Debug记录
            _logger.LogDebug("Canceling the message - processing cancellation token source _cts.");
            _cts.Cancel();
            _logger.LogDebug("Successfully canceled the message - processing cancellation token source _cts.");

            _inComingLowManager.Shutdown();
            _logger.LogDebug("All incoming low-priority message processing threads have been shut down.");

            _inComingMediumManager.Shutdown();
            _logger.LogDebug("All incoming medium-priority message processing threads have been shut down.");

            _inComingHighManager.Shutdown();
            _logger.LogDebug("All incoming high-priority message processing threads have been shut down.");

            _logger.LogInformation("All incoming message processing threads have been shut down.");

            _outComingHighManager.Shutdown();
            _logger.LogDebug("All outgoing high-priority message processing threads have been shut down.");

            _outComingMediumManager.Shutdown();
            _logger.LogDebug("All outgoing medium-priority message processing threads have been shut down.");

            _outComingLowManager.Shutdown();
            _logger.LogDebug("All outgoing low-priority message processing threads have been shut down.");

            _logger.LogInformation("All outgoing message processing threads have been shut down.");
        }
        public void StartInComingProcessing()
        {
            try
            {
                _logger.LogTrace("Entering StartProcessing method.");
                _logger.LogDebug("Initiating the initialization of message thread managers for different priorities.");

                // 启动高优先级消息的处理任务
                _logger.LogDebug("Starting the initialization of the high-priority message thread manager.");
                _inComingHighManager = new IncomingMessageThreadManager(
                    this,
                    _messageHighQueue,
                    _logger,
                    DataPriority.High,
                    minThreads: ConstantsConfig.In_High_MinThreadNum,
                    maxThreads: ConstantsConfig.In_High_MaxThreadNum);
                _logger.LogDebug("High-priority message thread manager initialization completed.");

                // 启动中优先级消息的处理任务
                _logger.LogDebug("Starting the initialization of the medium-priority message thread manager.");
                _inComingMediumManager = new IncomingMessageThreadManager(
                    this,
                    _messageMediumQueue,
                    _logger,
                    DataPriority.Medium,
                    minThreads: ConstantsConfig.In_Medium_MinThreadNum,
                    maxThreads: ConstantsConfig.In_Medium_MaxThreadNum);
                _logger.LogDebug("Medium-priority message thread manager initialization completed.");

                // 启动低优先级消息的处理任务
                _logger.LogDebug("Starting the initialization of the low-priority message thread manager.");
                _inComingLowManager = new IncomingMessageThreadManager(
                    this,
                    _messagelowQueue,
                    _logger,
                    DataPriority.Low,
                    minThreads: ConstantsConfig.In_Low_MinThreadNum,
                    maxThreads: ConstantsConfig.In_Low_MaxThreadNum);
                _logger.LogDebug("Low-priority message thread manager initialization completed.");

                _logger.LogDebug("Initialization of all priority message thread managers completed.");
                _logger.LogTrace("Exiting StartProcessing method.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while starting message processing consumers: {ex.Message}");
                _logger.LogWarning("Due to the error, some or all message thread managers may not have been initialized successfully.");
            }
        }

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

        private async Task ProcessPriorityMessages(DataPriority priority, ChannelReader<ClientMessage> reader, SemaphoreSlim semaphore)
        {
            // 异步遍历通道中的所有消息（支持取消令牌）
            await foreach (var message in reader.ReadAllAsync(_cts.Token))
            {
                // 验证消息优先级与处理器匹配（防御性编程）
                if (message.Data.Priority != priority)
                {
                    _logger.LogTrace($"Dropping message with mismatched priority: expected {priority}, actual {message.Data.Priority}");
                    continue;
                }

                _logger.LogDebug($"Received {priority} priority message: Id={message.Client.Id}, Size={MemoryCalculator.CalculateObjectSize(message.Data)} bytes");

                // 等待信号量（控制并发数）
                await semaphore.WaitAsync(_cts.Token);
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

        public async Task ProcessMessageWithPriority(ClientMessage message, DataPriority priority)
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

        private readonly ConcurrentDictionary<int, ConcurrentQueue<CommunicationData>> _ResumeMessages = new ConcurrentDictionary<int, ConcurrentQueue<CommunicationData>>();
        public async Task<bool> SendInfoDate(ClientConfig client, CommunicationData data)
        {
            bool result = false;
            switch (data.InfoType)
            {
                case InfoType.CtcNormal:
                case InfoType.CtcFile:
                    bool _isSend = false;
                    foreach (var item in _clients)
                    {
                        if (item.Value.UniqueId == data.Targetid)
                        {
                            // 发送信息
                            SendToClient(client.Id, data, data.Priority);
                            _isSend = true;
                            result = true;
                        }
                    }
                    if (!_isSend)
                    {
                        // 客户端不在线，将消息添加到待发送队列
                        var queue = _ResumeMessages.GetOrAdd(data.Targetid, new ConcurrentQueue<CommunicationData>());
                        queue.Enqueue(data);
                        result = true; // 消息入队视为操作成功
                    }
                    break;
                default:
                    result = await SendDate(client, data);
                    break;
            }

            return result;
        }

        private async Task SendPendingMessages(ClientConfig client, ConcurrentQueue<CommunicationData> queue)
        {
            _logger.LogTrace($"Starting to send pending messages for client {client.UniqueId}.");
            if (queue.IsEmpty)
            {
                _logger.LogDebug($"No pending messages found for client {client.UniqueId}.");
                return;
            }

            while (queue.TryDequeue(out var data))
            {
                try
                {
                    _logger.LogDebug($"Attempting to send message of type {data.InfoType} with priority {data.Priority} to client {client.UniqueId}.");
                    SendToClient(client.Id, data, data.Priority);
                    _logger.LogDebug($"Successfully sent message of type {data.InfoType} with priority {data.Priority} to client {client.UniqueId}.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to send message of type {data.InfoType} with priority {data.Priority} to client {client.UniqueId}. Error: {ex.Message}");
                }
            }
            _logger.LogTrace($"Finished sending all pending messages for client {client.UniqueId}.");
        }

        private async Task<bool> SendDate(ClientConfig client, CommunicationData data)
        {
            _logger.LogTrace($"Starting to send data to client {client.Id}.");

            try
            {
                // 1. 验证参数
                // 检查客户端套接字是否存在、是否连接以及数据是否为空
                if (client?.Socket == null || !client.Socket.Connected || data == null)
                {
                    _logger.LogWarning($"Client {client?.Id} Invalid parameters for SendData. Client socket is null or not connected, or data is null.");
                    return false;
                }

                _logger.LogTrace($"Client {client.Id} Parameters are valid. Proceeding to create protocol packet.");

                // 2. 获取配置(假设config是类成员变量或通过client获取)
                //var config = config ?? new ProtocolConfiguration();

                // 3. 创建协议数据包
                // 根据传入的数据和协议配置创建协议数据包
                var packet = new ProtocolPacketWrapper(
                    new ProtocolPacket()
                    {
                        Header = new ProtocolHeader { Version = 0x01, Reserved = ByteString.CopyFrom(new byte[3]) },
                        Data = data
                    },
                    ConstantsConfig.config);

                _logger.LogTrace($"Client {client.Id} Protocol packet created. Proceeding to serialize it.");

                // 4. 序列化为字节数组
                byte[] protocolBytes;
                try
                {
                    // 将协议数据包序列化为字节数组
                    protocolBytes = packet.ToBytes();
                    _logger.LogTrace($"Client {client.Id} Protocol packet serialized to {protocolBytes.Length} bytes.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Client {client.Id} Packet serialization failed: {ex.Message}. Stack trace: {ex.StackTrace}");
                    return false;
                }

                // 5. 发送数据(确保发送完整)
                // 初始化已发送的字节数
                int totalSent = 0;
                _logger.LogTrace($"Client {client.Id} Starting to send {protocolBytes.Length} bytes of data.");

                // 循环发送数据，直到所有数据都发送完毕
                while (totalSent < protocolBytes.Length)
                {
                    // 异步发送数据
                    int sent = await client.Socket.SendAsync(
                        new ArraySegment<byte>(protocolBytes, totalSent, protocolBytes.Length - totalSent),
                        SocketFlags.None);

                    _logger.LogTrace($"Client {client.Id} Sent {sent} bytes of data at offset {totalSent}.");

                    // 如果发送的字节数为 0，说明连接已经关闭
                    if (sent == 0)
                    {
                        _logger.LogWarning($"Client {client.Id} Connection closed during send. Sent {totalSent} bytes out of {protocolBytes.Length} bytes.");
                        return false;
                    }

                    // 更新已发送的字节数
                    totalSent += sent;
                }

                _logger.LogTrace($"Client {client.Id} Successfully sent {protocolBytes.Length} bytes of data.");

                // 6. 更新统计
                // 更新客户端发送的字节数统计信息
                client.AddSentBytes(protocolBytes.Length);
                _logger.LogTrace($"Client {client.Id} Updated sent bytes statistics. Total sent: {client.BytesSent} bytes.");

                return true;
            }
            catch (SocketException sex)
            {
                _logger.LogError($"Client {client.Id} Socket error in SendData: {sex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Client {client.Id} Unexpected error in SendData: {ex.Message}");
                return false;
            }
        }
    }
}
