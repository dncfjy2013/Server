using Protocol;
using Server.Client;
using Server.Core;
using Server.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static Server.Core.ServerInstance;

namespace Server.Common
{
    /// <summary>
    /// 动态线程管理器抽象基类，用于管理不同优先级的消息处理线程
    /// 支持根据消息队列长度自动扩容/缩容线程池
    /// </summary>
    /// <typeparam name="T">消息类型</typeparam>
    public abstract class DynamicThreadManagerBase<T>
    {
        /// <summary> 消息通道（用于线程间通信） </summary>
        protected readonly Channel<T> _channel;

        /// <summary> 线程安全锁 </summary>
        protected readonly object _lock = new();

        /// <summary> 当前活动线程数 </summary>
        protected int _currentThreadCount;

        /// <summary> 活动任务列表 </summary>
        protected readonly List<Task> _activeTasks = new();

        /// <summary> 取消令牌源 </summary>
        protected readonly CancellationTokenSource _cts = new();

        /// <summary> 日志记录器 </summary>
        protected readonly ILogger _logger;

        /// <summary> 最小线程数 </summary>
        protected readonly int _minThreads;

        /// <summary> 最大线程数 </summary>
        protected readonly int _maxThreads;

        /// <summary> 队列长度阈值（触发扩容的临界值） </summary>
        protected readonly int _queueThreshold;

        /// <summary> 监控间隔（毫秒） </summary>
        protected readonly int _monitorIntervalMs;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="channel">消息通道</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="minThreads">最小线程数</param>
        /// <param name="maxThreads">最大线程数</param>
        /// <param name="queueThreshold">队列阈值</param>
        /// <param name="monitorIntervalMs">监控间隔</param>
        public DynamicThreadManagerBase(
            Channel<T> channel,
            ILogger logger,
            int minThreads,
            int maxThreads,
            int queueThreshold,
            int monitorIntervalMs)
        {
            _channel = channel;
            _logger = logger;
            _minThreads = minThreads;
            _maxThreads = maxThreads;
            _queueThreshold = queueThreshold;
            _monitorIntervalMs = monitorIntervalMs;

            // 启动监控线程
            _logger.LogInformation($"DynamicThreadManagerBase initialized. MinThreads={minThreads}, MaxThreads={maxThreads}");
            StartMonitoring();
        }

        /// <summary>
        /// 启动监控循环：定期检查队列长度并调整线程数
        /// </summary>
        private void StartMonitoring()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogDebug("Monitoring loop started.");
                    while (!_cts.IsCancellationRequested)
                    {
                        // 等待监控间隔
                        await Task.Delay(_monitorIntervalMs, _cts.Token);

                        // 获取当前队列长度
                        int queueLength = _channel.Reader.Count;
                        _logger.LogTrace($"Queue length checked: {queueLength}");

                        // 调整线程数
                        AdjustThreadCount(queueLength);
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Monitoring loop cancelled.");
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"Monitoring loop failed with exception: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// 处理消息的抽象方法（由子类实现具体逻辑）
        /// </summary>
        /// <param name="message">待处理的消息</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>异步任务</returns>
        protected abstract Task ProcessMessageAsync(T message, CancellationToken ct);

        /// <summary>
        /// 调整线程数的模板方法（可被子类重写）
        /// </summary>
        /// <param name="queueLength">当前消息队列长度</param>
        protected virtual void AdjustThreadCount(int queueLength)
        {
            lock (_lock)
            {
                // 1. 扩容逻辑：队列长度超过阈值且未达最大线程数
                if (queueLength > _queueThreshold && _currentThreadCount < _maxThreads)
                {
                    int newThreads = Math.Min(_maxThreads - _currentThreadCount, 2); // 每次最多新增2个线程
                    for (int i = 0; i < newThreads; i++)
                    {
                        var ct = _cts.Token;
                        var task = Task.Run(() => ProcessMessageLoop(ct));
                        _activeTasks.Add(task);
                        _currentThreadCount++;

                        // 日志：记录扩容操作
                        _logger.LogInformation(
                            $"Thread added. Current: {_currentThreadCount}, Queue: {queueLength}, Priority: {typeof(T).Name}");
                    }
                }
                // 2. 缩容逻辑：队列长度低于阈值一半且超过最小线程数
                else if (_currentThreadCount > _minThreads && queueLength < _queueThreshold / 2)
                {
                    int newThreads = Math.Max(_currentThreadCount - _minThreads, 1); // 至少保留_minThreads个线程
                    for (int i = 0; i < newThreads; i++)
                    {
                        if (_activeTasks.Count == 0) break;

                        var task = _activeTasks.Last();
                        task.Dispose(); // 触发CancellationToken停止任务
                        _activeTasks.Remove(task);
                        _currentThreadCount--;

                        // 日志：记录缩容操作
                        _logger.LogWarning(
                            $"Thread removed. Current: {_currentThreadCount}, Queue: {queueLength}, Priority: {typeof(T).Name}");
                    }
                }
            }
        }

        /// <summary>
        /// 消息处理循环：持续从通道读取消息并处理
        /// </summary>
        /// <param name="ct">取消令牌</param>
        /// <returns>异步任务</returns>
        private async Task ProcessMessageLoop(CancellationToken ct)
        {
            try
            {
                _logger.LogDebug($"Worker thread started. Thread ID: {Environment.CurrentManagedThreadId}");

                // 持续读取消息直到通道关闭或取消
                await foreach (var msg in _channel.Reader.ReadAllAsync(ct))
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        // 处理消息（子类实现）
                        await ProcessMessageAsync(msg, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            $"Message processing failed. Thread ID: {Environment.CurrentManagedThreadId}, Error: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug($"Worker thread cancelled. Thread ID: {Environment.CurrentManagedThreadId}");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(
                    $"Worker thread terminated unexpectedly. Thread ID: {Environment.CurrentManagedThreadId}, Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止所有线程并释放资源
        /// </summary>
        public void Shutdown()
        {
            _cts.Cancel(); // 取消所有任务

            lock (_lock)
            {
                try
                {
                    // 等待所有任务完成
                    Task.WaitAll(_activeTasks.ToArray());
                    _activeTasks.Clear();
                    _currentThreadCount = 0;
                    _logger.LogInformation("All worker threads shutdown gracefully.");
                }
                catch (Exception ex)
                {
                    _logger.LogCritical($"Shutdown failed with exception: {ex.Message}");
                }
            }
        }
    }
}