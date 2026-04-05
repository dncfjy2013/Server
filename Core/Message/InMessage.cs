using Google.Protobuf;
using Logger;
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
    public partial class InMessage
    {

        private readonly ConcurrentDictionary<uint, ClientConfig> _clients;

        public Channel<ClientMessage> _messageHighQueue = Channel.CreateUnbounded<ClientMessage>();
        public Channel<ClientMessage> _messageMediumQueue = Channel.CreateUnbounded<ClientMessage>();
        public Channel<ClientMessage> _messagelowQueue = Channel.CreateUnbounded<ClientMessage>();

        private IncomingMessageThreadManager _inComingHighManager;
        private IncomingMessageThreadManager _inComingMediumManager;
        private IncomingMessageThreadManager _inComingLowManager;

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
        private FileMessage _fileMessage;
        private HeartbeatMessage _heartbeatMessage;
        private NormalMessage _normalMessage;

        public InMessage(ConcurrentDictionary<uint, ClientConfig> clients, ILogger logger, OutMessage outMessage)
        {
            _clients = clients;
            _logger = logger;
            _fileMessage = new FileMessage(_logger, _clients, outMessage);
            _heartbeatMessage = new HeartbeatMessage(_logger, outMessage);
            _normalMessage = new NormalMessage(_logger, outMessage);
        }

        
        public void Stop()
        {
            // 取消消息处理取消令牌源，同样是关键操作步骤，使用Debug记录
            _logger.Debug("Canceling the message - processing cancellation token source _cts.");
            _cts.Cancel();
            _logger.Debug("Successfully canceled the message - processing cancellation token source _cts.");

            _inComingLowManager.Shutdown();
            _logger.Debug("All incoming low-priority message processing threads have been shut down.");

            _inComingMediumManager.Shutdown();
            _logger.Debug("All incoming medium-priority message processing threads have been shut down.");

            _inComingHighManager.Shutdown();
            _logger.Debug("All incoming high-priority message processing threads have been shut down.");

            _logger.Info("All incoming message processing threads have been shut down.");
        }
        public void Start()
        {
            try
            {
                _logger.Trace("Entering StartProcessing method.");
                _logger.Debug("Initiating the initialization of message thread managers for different priorities.");

                // 启动高优先级消息的处理任务
                _logger.Debug("Starting the initialization of the high-priority message thread manager.");
                _inComingHighManager = new IncomingMessageThreadManager(
                    this,
                    _messageHighQueue,
                    _logger,
                    DataPriority.High,
                    minThreads: ConstantsConfig.In_High_MinThreadNum,
                    maxThreads: ConstantsConfig.In_High_MaxThreadNum);
                _logger.Debug("High-priority message thread manager initialization completed.");

                // 启动中优先级消息的处理任务
                _logger.Debug("Starting the initialization of the medium-priority message thread manager.");
                _inComingMediumManager = new IncomingMessageThreadManager(
                    this,
                    _messageMediumQueue,
                    _logger,
                    DataPriority.Medium,
                    minThreads: ConstantsConfig.In_Medium_MinThreadNum,
                    maxThreads: ConstantsConfig.In_Medium_MaxThreadNum);
                _logger.Debug("Medium-priority message thread manager initialization completed.");

                // 启动低优先级消息的处理任务
                _logger.Debug("Starting the initialization of the low-priority message thread manager.");
                _inComingLowManager = new IncomingMessageThreadManager(
                    this,
                    _messagelowQueue,
                    _logger,
                    DataPriority.Low,
                    minThreads: ConstantsConfig.In_Low_MinThreadNum,
                    maxThreads: ConstantsConfig.In_Low_MaxThreadNum);
                _logger.Debug("Low-priority message thread manager initialization completed.");

                _logger.Debug("Initialization of all priority message thread managers completed.");
                _logger.Trace("Exiting StartProcessing method.");
            }
            catch (Exception ex)
            {
                _logger.Error($"An error occurred while starting message processing consumers: {ex.Message}");
                _logger.Warn("Due to the error, some or all message thread managers may not have been initialized successfully.");
            }
        }

        public async Task ProcessMessages(DataPriority priority)
        {
            // 获取对应优先级的信号量（控制并发处理数量）
            var semaphore = _prioritySemaphores[priority];
            _logger.Debug($"Acquired semaphore for {priority} priority (CurrentCount={semaphore.CurrentCount})");

            try
            {
                switch (priority)
                {
                    case DataPriority.High:
                        _logger.Info($"High priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
                        await ProcessPriorityMessages(priority, _messageHighQueue.Reader, semaphore);
                        break;
                    case DataPriority.Medium:
                        _logger.Info($"Medium priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
                        await ProcessPriorityMessages(priority, _messageMediumQueue.Reader, semaphore);
                        break;
                    case DataPriority.Low:
                        _logger.Info($"Low priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
                        await ProcessPriorityMessages(priority, _messagelowQueue.Reader, semaphore);
                        break;
                    default:
                        _logger.Warn($"Unknown priority {priority} received, skipping processor");
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Debug($"{priority} priority processor was canceled");
            }
            catch (Exception ex)
            {
                _logger.Critical($"Unhandled exception in {priority} processor: {ex.Message}  ");
            }
            finally
            {
                _logger.Info($"{priority} priority processor stopped");
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
                    _logger.Trace($"Dropping message with mismatched priority: expected {priority}, actual {message.Data.Priority}");
                    continue;
                }

                _logger.Debug($"Received {priority} priority message: Id={message.Client.Id}, Size={MemoryCalculator.CalculateObjectSize(message.Data)} bytes");

                // 等待信号量（控制并发数）
                await semaphore.WaitAsync(_cts.Token);
                _logger.Trace($"{priority} semaphore acquired (current count={semaphore.CurrentCount})");

                try
                {
                    // 处理消息核心逻辑（假设包含业务处理和耗时操作）
                    await ProcessMessageWithPriority(message, priority);
                    _logger.Debug($"{priority} message processed successfully: Id={message.Client.Id}");
                }
                catch (TimeoutException tex)
                {
                    _logger.Error($"Timeout processing {priority} message {message.Client.Id}: {tex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error processing {priority} message {message.Client.Id}: {ex.Message}  ");
                    if (priority == DataPriority.High)
                    {
                        _logger.Warn($"High-priority message failed: {message.Client.Id}, will retry later");
                    }
                }
                finally
                {
                    semaphore.Release();
                    _logger.Trace($"{priority} semaphore released (current count={semaphore.CurrentCount})");
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
            _logger.Debug($"Set {priority} priority message timeout to {timeout.TotalMilliseconds}ms");

            // 使用CancellationTokenSource实现处理超时控制
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                _logger.Trace($"Start processing {priority} message (Id={message.Client.Id}, Type={message.Data.InfoType})");

                // 根据消息类型分发不同的处理逻辑
                switch (message.Data.InfoType)
                {
                    case InfoType.HeartBeat:
                        _logger.Debug($"Handling heartbeat from client {message.Client.Id}");
                        await _heartbeatMessage.HandleHeartbeat(message.Client, message.Data);
                        _logger.Debug($"Heartbeat handled successfully for client {message.Client.Id}");
                        break;

                    case InfoType.CtsFile:
                        _logger.Debug($"Handling file transfer for client {message.Client.Id} (Size={MemoryCalculator.CalculateObjectSize(message.Data)} bytes)");
                        await _fileMessage.HandleFileTransfer(message.Client, message.Data);
                        _logger.Debug($"File transfer completed for client {message.Client.Id}");
                        break;

                    case InfoType.CtsNormal:
                        _logger.Debug($"Handling normal message for client {message.Client.Id} (Content={message.Data.Message})");
                        await _normalMessage.HandleNormalMessage(message.Client, message.Data);
                        _logger.Debug($"Normal message processed for client {message.Client.Id}");
                        break;
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // 处理超时异常（优先级相关）
                _logger.Error($"{priority} priority message processing timed out (Id={message.Client.Id})");
                _logger.Warn($"Client {message.Client.Id} may experience delay due to timeout");
            }
            catch (Exception ex)
            {
                // 处理其他异常（非超时原因）
                _logger.Error($"Unhandled error processing {priority} message (Id={message.Client.Id}): {ex.Message}  ");
                if (priority == DataPriority.High)
                {
                    _logger.Critical($"High-priority message failure requires immediate attention: {message.Client.Id}");
                }
            }
        }

    }
}
