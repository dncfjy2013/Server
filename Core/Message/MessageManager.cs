//using Core.Message.ThreadManager;
//using Protocol;
//using Server.Core.Common;
//using Server.Core.Config;
//using Server.Utils;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Channels;
//using System.Threading.Tasks;

//namespace Core.Message
//{
//    public class MessageManager
//    {
//        // 创建一个无界的通道用于存储高优先级的客户端消息
//        // 通道是一种用于在不同线程或任务之间安全传递数据的机制
//        private Channel<ClientMessage> _messageHighQueue = Channel.CreateUnbounded<ClientMessage>();

//        // 创建一个无界的通道用于存储中优先级的客户端消息
//        private Channel<ClientMessage> _messageMediumQueue = Channel.CreateUnbounded<ClientMessage>();

//        // 创建一个无界的通道用于存储低优先级的客户端消息
//        private Channel<ClientMessage> _messagelowQueue = Channel.CreateUnbounded<ClientMessage>();

//        /// <summary>
//        /// 高优先级消息的线程管理器实例。负责管理和处理高优先级的客户端消息队列。
//        /// 该管理器会根据系统的 CPU 核心数动态调整线程数量，以确保高优先级消息能够被快速处理。
//        /// 其最小线程数设置为当前系统的 CPU 核心数，最大线程数为 CPU 核心数的两倍。
//        /// </summary>
//        private IncomingMessageThreadManager _incomingHighManager;

//        /// <summary>
//        /// 中优先级消息的线程管理器实例。用于管理和处理中优先级的客户端消息队列。
//        /// 为了平衡系统资源分配，该管理器启动的线程数量为 CPU 核心数的一半，
//        /// 最小线程数是 CPU 核心数的一半，最大线程数为 CPU 核心数。
//        /// </summary>
//        private IncomingMessageThreadManager _incomingMediumManager;

//        /// <summary>
//        /// 低优先级消息的线程管理器实例。主要负责处理低优先级的客户端消息队列。
//        /// 由于低优先级消息对处理及时性要求较低，所以该管理器只启动较少的线程，
//        /// 最小线程数为 1，最大线程数为 2。
//        /// </summary>
//        private IncomingMessageThreadManager _incomingLowManager;

//        // 定义一个字典，用于存储不同优先级对应的信号量
//        // 信号量用于控制并发访问的数量，确保系统资源不会被过度占用
//        private readonly Dictionary<DataPriority, SemaphoreSlim> _prioritySemaphores = new()
//        {
//            // 为高优先级消息设置信号量，允许的并发数量为处理器核心数的两倍
//            [DataPriority.High] = new SemaphoreSlim(ConstantsConfig.High_Min_Semaphores, ConstantsConfig.High_Max_Semaphores),
//            // 为中优先级消息设置信号量，允许的并发数量等于处理器核心数
//            [DataPriority.Medium] = new SemaphoreSlim(ConstantsConfig.Medium_Min_Semaphores, ConstantsConfig.Medium_Max_Semaphores),
//            // 为低优先级消息设置信号量，允许的并发数量为处理器核心数的一半
//            [DataPriority.Low] = new SemaphoreSlim(ConstantsConfig.Low_Min_Semaphores, ConstantsConfig.Low_Max_Semaphores)
//        };
//        // 创建一个用于取消消息处理任务的 CancellationTokenSource
//        // 当需要停止消息处理时，可以调用该对象的 Cancel 方法来取消相关任务
//        private readonly CancellationTokenSource _processingCts = new();

//        private ILogger _logger;

//        public MessageManager(ILogger logger, Channel<ClientMessage> messageHighQueue, Channel<ClientMessage> messageMediumQueue, Channel<ClientMessage> messagelowQueue, IncomingMessageThreadManager incomingHighManager, IncomingMessageThreadManager incomingMediumManager, IncomingMessageThreadManager incomingLowManager, Dictionary<DataPriority, SemaphoreSlim> prioritySemaphores)
//        {
//            _logger = logger;

//            _messageHighQueue = messageHighQueue;
//            _messageMediumQueue = messageMediumQueue;
//            _messagelowQueue = messagelowQueue;
//            _incomingHighManager = incomingHighManager;
//            _incomingMediumManager = incomingMediumManager;
//            _incomingLowManager = incomingLowManager;
//            _prioritySemaphores = prioritySemaphores;
//        }



//        /// <summary>
//        /// 启动消息处理消费者。此方法应在服务启动时调用，根据 CPU 核心数为不同优先级的消息队列启动相应数量的消费者任务。
//        /// </summary>
//        public void StartProcessing()
//        {
//            try
//            {
//                _logger.LogTrace("Entering StartProcessing method.");
//                _logger.LogDebug("Initiating the initialization of message thread managers for different priorities.");

//                // 启动高优先级消息的处理任务
//                _logger.LogDebug("Starting the initialization of the high-priority message thread manager.");
//                _incomingHighManager = new IncomingMessageThreadManager(
//                    this,
//                    _messageHighQueue,
//                    _logger,
//                    DataPriority.High,
//                    minThreads: ConstantsConfig.In_High_MinThreadNum,
//                    maxThreads: ConstantsConfig.In_High_MaxThreadNum);
//                _logger.LogDebug("High-priority message thread manager initialization completed.");

//                // 启动中优先级消息的处理任务
//                _logger.LogDebug("Starting the initialization of the medium-priority message thread manager.");
//                _incomingMediumManager = new IncomingMessageThreadManager(
//                    this,
//                    _messageMediumQueue,
//                    _logger,
//                    DataPriority.Medium,
//                    minThreads: ConstantsConfig.In_Medium_MinThreadNum,
//                    maxThreads: ConstantsConfig.In_Medium_MaxThreadNum);
//                _logger.LogDebug("Medium-priority message thread manager initialization completed.");

//                // 启动低优先级消息的处理任务
//                _logger.LogDebug("Starting the initialization of the low-priority message thread manager.");
//                _incomingLowManager = new IncomingMessageThreadManager(
//                    this,
//                    _messagelowQueue,
//                    _logger,
//                    DataPriority.Low,
//                    minThreads: ConstantsConfig.In_Low_MinThreadNum,
//                    maxThreads: ConstantsConfig.In_Low_MaxThreadNum);
//                _logger.LogDebug("Low-priority message thread manager initialization completed.");

//                _logger.LogDebug("Initialization of all priority message thread managers completed.");
//                _logger.LogTrace("Exiting StartProcessing method.");
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError($"An error occurred while starting message processing consumers: {ex.Message}");
//                _logger.LogWarning("Due to the error, some or all message thread managers may not have been initialized successfully.");
//            }
//        }

//        /// <summary>
//        /// 处理不同优先级的客户端消息（消费者核心逻辑）
//        /// </summary>
//        /// <param name="priority">消息优先级</param>
//        public async Task ProcessMessages(DataPriority priority)
//        {
//            // 获取对应优先级的信号量（控制并发处理数量）
//            var semaphore = _prioritySemaphores[priority];
//            _logger.LogDebug($"Acquired semaphore for {priority} priority (CurrentCount={semaphore.CurrentCount})");

//            try
//            {
//                switch (priority)
//                {
//                    case DataPriority.High:
//                        _logger.LogInformation($"High priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
//                        await ProcessPriorityMessages(priority, _messageHighQueue.Reader, semaphore);
//                        break;
//                    case DataPriority.Medium:
//                        _logger.LogInformation($"Medium priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
//                        await ProcessPriorityMessages(priority, _messageMediumQueue.Reader, semaphore);
//                        break;
//                    case DataPriority.Low:
//                        _logger.LogInformation($"Low priority message processor started (ThreadId={Environment.CurrentManagedThreadId})");
//                        await ProcessPriorityMessages(priority, _messagelowQueue.Reader, semaphore);
//                        break;
//                    default:
//                        _logger.LogWarning($"Unknown priority {priority} received, skipping processor");
//                        return;
//                }
//            }
//            catch (OperationCanceledException)
//            {
//                _logger.LogDebug($"{priority} priority processor was canceled");
//            }
//            catch (Exception ex)
//            {
//                _logger.LogCritical($"Unhandled exception in {priority} processor: {ex.Message}  ");
//            }
//            finally
//            {
//                _logger.LogInformation($"{priority} priority processor stopped");
//            }
//        }

//        /// <summary>
//        /// 处理特定优先级消息的通用逻辑
//        /// </summary>
//        /// <param name="priority">消息优先级</param>
//        /// <param name="reader">通道读取器</param>
//        private async Task ProcessPriorityMessages(DataPriority priority, ChannelReader<ClientMessage> reader, SemaphoreSlim semaphore)
//        {
//            // 异步遍历通道中的所有消息（支持取消令牌）
//            await foreach (var message in reader.ReadAllAsync(_processingCts.Token))
//            {
//                // 验证消息优先级与处理器匹配（防御性编程）
//                if (message.Data.Priority != priority)
//                {
//                    _logger.LogTrace($"Dropping message with mismatched priority: expected {priority}, actual {message.Data.Priority}");
//                    continue;
//                }

//                _logger.LogDebug($"Received {priority} priority message: Id={message.Client.Id}, Size={MemoryCalculator.CalculateObjectSize(message.Data)} bytes");

//                // 等待信号量（控制并发数）
//                await semaphore.WaitAsync(_processingCts.Token);
//                _logger.LogTrace($"{priority} semaphore acquired (current count={semaphore.CurrentCount})");

//                try
//                {
//                    // 处理消息核心逻辑（假设包含业务处理和耗时操作）
//                    await ProcessMessageWithPriority(message, priority);
//                    _logger.LogDebug($"{priority} message processed successfully: Id={message.Client.Id}");
//                }
//                catch (TimeoutException tex)
//                {
//                    _logger.LogError($"Timeout processing {priority} message {message.Client.Id}: {tex.Message}");
//                }
//                catch (Exception ex)
//                {
//                    _logger.LogError($"Error processing {priority} message {message.Client.Id}: {ex.Message}  ");
//                    if (priority == DataPriority.High)
//                    {
//                        _logger.LogWarning($"High-priority message failed: {message.Client.Id}, will retry later");
//                    }
//                }
//                finally
//                {
//                    semaphore.Release();
//                    _logger.LogTrace($"{priority} semaphore released (current count={semaphore.CurrentCount})");
//                }
//            }
//        }

//        /// <summary>
//        /// 按优先级处理客户端消息（核心业务逻辑）
//        /// </summary>
//        /// <param name="message">待处理的客户端消息</param>
//        /// <param name="priority">消息优先级</param>
//        private async Task ProcessMessageWithPriority(ClientMessage message, DataPriority priority)
//        {
//            // 根据优先级设置不同的处理超时时间
//            var timeout = priority switch
//            {
//                DataPriority.High => TimeSpan.FromMilliseconds(100),  // 高优先级消息要求快速响应
//                DataPriority.Medium => TimeSpan.FromMilliseconds(500), // 中等优先级允许稍长处理时间
//                _ => TimeSpan.FromSeconds(1)                           // 低优先级默认1秒超时
//            };
//            _logger.LogDebug($"Set {priority} priority message timeout to {timeout.TotalMilliseconds}ms");

//            // 使用CancellationTokenSource实现处理超时控制
//            using var cts = new CancellationTokenSource(timeout);

//            try
//            {
//                _logger.LogTrace($"Start processing {priority} message (Id={message.Client.Id}, Type={message.Data.InfoType})");

//                // 根据消息类型分发不同的处理逻辑
//                switch (message.Data.InfoType)
//                {
//                    case InfoType.HeartBeat:
//                        _logger.LogDebug($"Handling heartbeat from client {message.Client.Id}");
//                        await HandleHeartbeat(message.Client, message.Data);
//                        _logger.LogDebug($"Heartbeat handled successfully for client {message.Client.Id}");
//                        break;

//                    case InfoType.CtsFile:
//                        _logger.LogDebug($"Handling file transfer for client {message.Client.Id} (Size={MemoryCalculator.CalculateObjectSize(message.Data)} bytes)");
//                        await HandleFileTransfer(message.Client, message.Data);
//                        _logger.LogDebug($"File transfer completed for client {message.Client.Id}");
//                        break;

//                    case InfoType.CtsNormal:
//                        _logger.LogDebug($"Handling normal message for client {message.Client.Id} (Content={message.Data.Message})");
//                        await HandleNormalMessage(message.Client, message.Data);
//                        _logger.LogDebug($"Normal message processed for client {message.Client.Id}");
//                        break;
//                }

//                _logger.LogTrace($"Completed processing {priority} message (Id={message.Client.Id})");
//            }
//            catch (OperationCanceledException) when (cts.IsCancellationRequested)
//            {
//                // 处理超时异常（优先级相关）
//                _logger.LogError($"{priority} priority message processing timed out (Id={message.Client.Id})");
//                _logger.LogWarning($"Client {message.Client.Id} may experience delay due to timeout");
//            }
//            catch (Exception ex)
//            {
//                // 处理其他异常（非超时原因）
//                _logger.LogError($"Unhandled error processing {priority} message (Id={message.Client.Id}): {ex.Message}  ");
//                if (priority == DataPriority.High)
//                {
//                    _logger.LogCritical($"High-priority message failure requires immediate attention: {message.Client.Id}");
//                }
//            }
//        }

//    }
//}
