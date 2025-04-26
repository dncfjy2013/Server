using Protocol;
using Server.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Server.Core
{
    partial class Server
    {
        private readonly int MaxQueueSize = int.MaxValue;

        private Channel<ClientMessage> _messageHighQueue = Channel.CreateUnbounded<ClientMessage>();
        private Channel<ClientMessage> _messageMediumQueue = Channel.CreateUnbounded<ClientMessage>();
        private Channel<ClientMessage> _messagelowQueue = Channel.CreateUnbounded<ClientMessage>();

        private readonly Dictionary<DataPriority, SemaphoreSlim> _prioritySemaphores = new()
        {
            [DataPriority.High] = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2),
            [DataPriority.Medium] = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount),
            [DataPriority.Low] = new SemaphoreSlim(Environment.ProcessorCount / 2, Environment.ProcessorCount / 2)
        };

        private readonly CancellationTokenSource _processingCts = new();

        // 启动消费者（在适当位置调用，如服务启动时）
        public void StartProcessing()
        {
            // 根据CPU核心数设置消费者数量
            // Start high priority processors
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                logger.LogInformation($"Start high priority processors Seq {i}");
                _ = Task.Run(() => ProcessMessages(DataPriority.High));
            }

            // Start medium priority processors
            for (int i = 0; i < Environment.ProcessorCount / 2; i++)
            {
                logger.LogInformation($"Start medium priority processors Seq {i}");
                _ = Task.Run(() => ProcessMessages(DataPriority.Medium));
            }

            // Start low priority processor
            logger.LogInformation($"Start low priority processors");
            _ = Task.Run(() => ProcessMessages(DataPriority.Low));
        }

        // 新增消费者处理方法
        private async Task ProcessMessages(DataPriority priority)
        {
            var semaphore = _prioritySemaphores[priority];

            switch (priority)
            {
                case DataPriority.High:
                    await foreach (var message in _messageHighQueue.Reader.ReadAllAsync(_processingCts.Token))
                    {
                        if (message.Data.Priority != priority) continue;

                        await semaphore.WaitAsync(_processingCts.Token);
                        try
                        {
                            await ProcessMessageWithPriority(message, priority);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error processing {priority} priority message: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    break;
                case DataPriority.Medium:
                    await foreach (var message in _messageMediumQueue.Reader.ReadAllAsync(_processingCts.Token))
                    {
                        if (message.Data.Priority != priority) continue;

                        await semaphore.WaitAsync(_processingCts.Token);
                        try
                        {
                            await ProcessMessageWithPriority(message, priority);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error processing {priority} priority message: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    break;
                case DataPriority.Low:
                    await foreach (var message in _messagelowQueue.Reader.ReadAllAsync(_processingCts.Token))
                    {
                        if (message.Data.Priority != priority) continue;

                        await semaphore.WaitAsync(_processingCts.Token);
                        try
                        {
                            await ProcessMessageWithPriority(message, priority);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error processing {priority} priority message: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    break;
            }
        }
        private async Task ProcessMessageWithPriority(ClientMessage message, DataPriority priority)
        {
            var timeout = priority switch
            {
                DataPriority.High => TimeSpan.FromMilliseconds(100),
                DataPriority.Medium => TimeSpan.FromMilliseconds(500),
                _ => TimeSpan.FromSeconds(1)
            };

            using var cts = new CancellationTokenSource(timeout);

            try
            {
                switch (message.Data.InfoType)
                {
                    case InfoType.HeartBeat:
                        await HandleHeartbeat(message.Client, message.Data);
                        break;
                    case InfoType.File:
                        await HandleFileTransfer(message.Client, message.Data);
                        break;
                    case InfoType.Ack:
                        break;
                    default:
                        await HandleNormalMessage(message.Client, message.Data);
                        break;
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                logger.LogError($"{priority} priority message processing timeout");
            }
        }

        // 修改后的HandleClient方法（生产者）
        private async Task HandleClient(ClientConfig client)
        {
            try
            {
                Stream stream = client.Socket != null
                    ? new NetworkStream(client.Socket)
                    : client.SslStream;

                while (_isRunning)
                {
                    try
                    {
                        byte[] headerBuffer = new byte[8];
                        // 1. 接收头部
                        if (!await ReadFullAsync(stream, headerBuffer, 8))
                        {
                            logger.LogWarning($"Client {client.Id} disconnected while reading header");
                            return;
                        }

                        // 2. 解析头部
                        if (!ProtocolHeaderExtensions.TryFromBytes(headerBuffer, out var header))
                        {
                            logger.LogWarning($"Client {client.Id} Invalid header received");
                            continue;
                        }

                        // 3. 版本检查
                        if (!config.SupportedVersions.Contains((byte)header.Version))
                        {
                            logger.LogWarning($"Client {client.Id} Unsupported protocol version: {header.Version}");
                            continue;
                        }

                        // 4. 接收数据体
                        byte[] payloadBuffer = new byte[header.MessageLength];
                        if (!await ReadFullAsync(stream, payloadBuffer, (int)header.MessageLength))
                        {
                            logger.LogWarning($"Client {client.Id}  disconnected while reading payload");
                            return;
                        }

                        // 5. 组合完整数据包
                        byte[] fullPacket = new byte[8 + header.MessageLength];
                        Buffer.BlockCopy(headerBuffer, 0, fullPacket, 0, 8);
                        Buffer.BlockCopy(payloadBuffer, 0, fullPacket, 8, (int)header.MessageLength);

                        // 6. 解析数据包
                        var (success, packet, error) = ProtocolPacketWrapper.TryFromBytes(fullPacket);
                        if (!success)
                        {
                            logger.LogWarning($"Client {client.Id} Failed to parse packet: {error}");
                            continue;
                        }

                        client.UpdateActivity();
                        // 处理有效数据包
                        var message = new ClientMessage
                        {
                            Client = client,
                            Data = packet.Data,
                            ReceivedTime = DateTime.Now
                        };
                        switch (packet.Data.Priority)
                        {
                            case DataPriority.Low:
                                await _messagelowQueue.Writer.WriteAsync(message);
                                break;
                            case DataPriority.High:
                                await _messageHighQueue.Writer.WriteAsync(message);
                                break;
                            case DataPriority.Medium:
                                await _messageMediumQueue.Writer.WriteAsync(message);
                                break;
                        }

                        // 控制队列积压（可选）
                        if (_messagelowQueue.Reader.Count > MaxQueueSize)
                        {
                            logger.LogWarning($"Client {client.Id} message low queue growing");
                            // 可在此处实施背压策略，如暂停接收等
                        }
                        if (_messageMediumQueue.Reader.Count > MaxQueueSize)
                        {
                            logger.LogWarning($"Client {client.Id} message medium queue growing");
                            // 可在此处实施背压策略，如暂停接收等
                        }
                        if (_messageHighQueue.Reader.Count > MaxQueueSize)
                        {
                            logger.LogWarning($"Client {client.Id} message high queue growing");
                            // 可在此处实施背压策略，如暂停接收等
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Client {client.Id} error: {ex.Message}");
                        break; // 发生异常时退出循环
                    }
                }
            }
            finally
            {
                DisconnectClient(client.Id);
            }
        }
    }
}
