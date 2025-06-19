using Google.Protobuf;
using Logger;
using MySqlX.XDevAPI;
using Protocol;
using Server.Core.Common;
using Server.Core.Config;
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
    public class OutMessage
    {
        private ILogger _logger;
        private readonly ConcurrentDictionary<uint, ClientConfig> _clients;

        private OutgoingMessageThreadManager _outComingHighManager;
        private OutgoingMessageThreadManager _outComingMediumManager;
        private OutgoingMessageThreadManager _outComingLowManager;

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

        private readonly ConcurrentDictionary<int, ConcurrentQueue<CommunicationData>> _ResumeMessages = new ConcurrentDictionary<int, ConcurrentQueue<CommunicationData>>();
        private readonly CancellationTokenSource _cts = new();

        public OutMessage(ILogger logger, ConcurrentDictionary<uint, ClientConfig> clients)
        {
            _logger = logger;
            _clients = clients;
        }

        public void Start()
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
        public void Stop()
        {
            // 取消消息处理取消令牌源，同样是关键操作步骤，使用Debug记录
            _logger.LogDebug("Canceling the message - processing cancellation token source _cts.");
            _cts.Cancel();
            _logger.LogDebug("Successfully canceled the message - processing cancellation token source _cts.");

            _outComingHighManager.Shutdown();
            _logger.LogDebug("All outgoing high-priority message processing threads have been shut down.");

            _outComingMediumManager.Shutdown();
            _logger.LogDebug("All outgoing medium-priority message processing threads have been shut down.");

            _outComingLowManager.Shutdown();
            _logger.LogDebug("All outgoing low-priority message processing threads have been shut down.");

            _logger.LogInformation("All outgoing message processing threads have been shut down.");
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

    // 定义主动消息结构体
    public class ServerOutgoingMessage
    {
        public CommunicationData Data { get; set; }
        public int RetryCount { get; set; } = 0; // 重传次数
        public DateTime SentTime { get; set; } // 发送时间
        public DataPriority Priority { get; set; } // 优先级
    }
}
