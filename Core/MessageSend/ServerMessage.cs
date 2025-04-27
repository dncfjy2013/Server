using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Protocol;
using Server.Common;
using Server.Extend;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Server.Core
{
    // 定义主动消息结构体
    public class ServerOutgoingMessage
    {
        public int ClientId { get; set; }
        public CommunicationData Data { get; set; }
        public int RetryCount { get; set; } = 0; // 重传次数
        public DateTime SentTime { get; set; } // 发送时间
        public DataPriority Priority { get; set; } // 优先级
    }

    partial class ServerInstance
    {
        // 在Server类中新增字段
        private readonly Channel<ServerOutgoingMessage> _outgoingHighMessages =
            Channel.CreateUnbounded<ServerOutgoingMessage>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly Channel<ServerOutgoingMessage> _outgoingMedumMessages =
            Channel.CreateUnbounded<ServerOutgoingMessage>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly Channel<ServerOutgoingMessage> _outgoingLowMessages =
            Channel.CreateUnbounded<ServerOutgoingMessage>(new UnboundedChannelOptions { SingleWriter = true });

        // 在Server类中新增字段
        private readonly Dictionary<DataPriority, (int MaxRetries, TimeSpan Interval)> _retryPolicies = new() {
                { DataPriority.High, (MaxRetries: 5, Interval: TimeSpan.FromSeconds(5)) }, // 高优先级：3次重试，间隔10秒
                { DataPriority.Medium, (MaxRetries: 3, Interval: TimeSpan.FromSeconds(10)) }, // 中优先级：2次重试，间隔20秒
                { DataPriority.Low, (MaxRetries: 1, Interval: TimeSpan.FromSeconds(15)) } }; // 低优先级：1次重试，间隔30秒

        private OutgoingMessageThreadManager _highPriorityManager;
        private OutgoingMessageThreadManager _mediumPriorityManager;
        private OutgoingMessageThreadManager _lowPriorityManager;

        // 启动不同优先级消息处理任务
        public void StartOutgoingMessageProcessing()
        {
            int Threads = Environment.ProcessorCount;

            _highPriorityManager = new OutgoingMessageThreadManager(
                this,
                _outgoingHighMessages,
                logger,
                DataPriority.High,
                0,
                Threads * 2);

            _mediumPriorityManager = new OutgoingMessageThreadManager(
                this,
                _outgoingMedumMessages,
                logger,
                DataPriority.Medium,
                0,
                Threads);

            _lowPriorityManager = new OutgoingMessageThreadManager(
                this,
                _outgoingLowMessages,
                logger,
                DataPriority.Low,
                0,
                Threads / 2);
        }

        public async Task ProcessOutgoingMessages(Channel<ServerOutgoingMessage> messageChannel, DataPriority priority)
        {
            await foreach (var msg in messageChannel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    if (!_clients.TryGetValue(msg.ClientId, out var client)) continue;

                    // 发送消息
                    bool sent = await SendData(client, msg.Data);
                    if (!sent) throw new Exception("Send failed");

                    msg.SentTime = DateTime.Now;
                    logger.LogInformation($"Sent {msg.Data.InfoType} to {client.Id} (Priority: {priority})");

                    // 记录发送成功，无需重传
                    if (msg.Data.InfoType == InfoType.StcFile && msg.Data.Message == "FILE_COMPLETE")
                    {
                        // 文件完成消息无需重传
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    // 触发重传逻辑
                    await HandleRetry(msg);
                }
            }
        }

        private async Task HandleRetry(ServerOutgoingMessage msg)
        {
            var policy = _retryPolicies[msg.Priority];
            if (msg.RetryCount >= policy.MaxRetries)
            {
                logger.LogError($"Max retries exceeded for message to {msg.ClientId}. Dropping.");
                return;
            }

            msg.RetryCount++;
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
            logger.LogWarning($"Retrying message to {msg.ClientId} (Retry {msg.RetryCount}/{policy.MaxRetries})");
        }

        // 主动发送消息（支持指定优先级）
        public void SendToClient(int clientId, CommunicationData data, DataPriority priority = DataPriority.Medium)
        {
            if (!_clients.TryGetValue(clientId, out var client)) return;
            switch (priority)
            {
                case DataPriority.High:
                    _outgoingHighMessages.Writer.TryWrite(new ServerOutgoingMessage
                    {
                        ClientId = clientId,
                        Data = data,
                        SentTime = DateTime.Now
                    });
                    break;
                case DataPriority.Medium:
                    _outgoingMedumMessages.Writer.TryWrite(new ServerOutgoingMessage
                    {
                        ClientId = clientId,
                        Data = data,
                        SentTime = DateTime.Now
                    });
                    break;
                case DataPriority.Low:
                    _outgoingLowMessages.Writer.TryWrite(new ServerOutgoingMessage
                    {
                        ClientId = clientId,
                        Data = data,
                        SentTime = DateTime.Now
                    });
                    break;
            }
            
        }

        // 主动发送文件（拆分文件块并指定高优先级）
        public async Task SendFileAsync(int clientId, string filePath, DataPriority priority = DataPriority.High)
        {
            if (!_clients.TryGetValue(clientId, out var client)) return;
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return;

            // 拆分文件块（示例：每块1MB）
            const int chunkSize = 1024 * 1024;
            var fileId = Guid.NewGuid().ToString();
            var totalChunks = (int)Math.Ceiling((double)fileInfo.Length / chunkSize);

            using var fs = fileInfo.OpenRead();
            var buffer = new byte[chunkSize];
            int chunkIndex = 0;

            while (chunkIndex < totalChunks)
            {
                int read = await fs.ReadAsync(buffer, 0, chunkSize);
                var chunkData = buffer.AsMemory(0, read);

                var data = new CommunicationData
                {
                    InfoType = InfoType.StcFile,
                    FileId = fileId,
                    FileSize = fileInfo.Length,
                    TotalChunks = totalChunks,
                    ChunkIndex = chunkIndex,
                    ChunkData = ByteString.CopyFrom(chunkData.ToArray()),
                    ChunkMd5 = CalculateChunkHash(chunkData.ToArray()), // 计算块MD5
                    Priority = priority // 高优先级确保及时传输
                };

                SendToClient(clientId, data, priority);
                chunkIndex++;
            }

            // 发送文件完成标记（高优先级）
            SendToClient(clientId, new CommunicationData
            {
                InfoType = InfoType.StcFile,
                FileId = fileId,
                Message = "FILE_COMPLETE",
                Priority = priority
            }, priority);
        }
    }
}
