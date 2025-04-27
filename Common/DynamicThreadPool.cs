using Protocol;
using Server.Client;
using Server.Core;
using Server.Extend;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static Server.Core.ServerInstance;

namespace Server.Common
{
    // 抽象基类：动态线程管理器
    public abstract class DynamicThreadManagerBase<T>
    {
        protected readonly Channel<T> _channel;
        protected readonly object _lock = new();
        protected int _currentThreadCount;
        protected readonly List<Task> _activeTasks = new();
        protected readonly CancellationTokenSource _cts = new();
        protected readonly ILogger _logger;
        protected readonly int _minThreads;
        protected readonly int _maxThreads;
        protected readonly int _queueThreshold;
        protected readonly int _monitorIntervalMs;

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
            StartMonitoring();
        }

        // 启动监控循环
        private void StartMonitoring()
        {
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_monitorIntervalMs, _cts.Token);
                        int queueLength = _channel.Reader.Count;
                        AdjustThreadCount(queueLength);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // 优雅退出
                    }
                }
            });
        }

        // 抽象方法：处理消息（由子类实现）
        protected abstract Task ProcessMessageAsync(T message, CancellationToken ct);

        // 调整线程数（模板方法）
        protected virtual void AdjustThreadCount(int queueLength)
        {
            lock (_lock)
            {
                // 扩容逻辑
                if (queueLength > _queueThreshold && _currentThreadCount < _maxThreads)
                {
                    int newThreads = Math.Min(_maxThreads - _currentThreadCount, 2);
                    for (int i = 0; i < newThreads; i++)
                    {
                        var ct = _cts.Token;
                        var task = Task.Run(() => ProcessMessageLoop(ct));
                        _activeTasks.Add(task);
                        _currentThreadCount++;
                        _logger.LogInformation($"Thread added. Current: {_currentThreadCount}, Queue: {queueLength}");
                    }
                }
                // 缩容逻辑
                else if (_currentThreadCount > _minThreads && queueLength < _queueThreshold / 2)
                {
                    int newThreads = Math.Max(_currentThreadCount - _minThreads, 1);
                    for (int i = 0; i < newThreads; i++)
                    {
                        if (_activeTasks.Count == 0) break;
                        var task = _activeTasks.Last();
                        task.Dispose(); // 触发CancellationToken
                        _activeTasks.Remove(task);
                        _currentThreadCount--;
                        _logger.LogInformation($"Thread removed. Current: {_currentThreadCount}, Queue: {queueLength}");
                    }
                }
            }
        }

        // 消息处理循环
        private async Task ProcessMessageLoop(CancellationToken ct)
        {
            try
            {
                await foreach (var msg in _channel.Reader.ReadAllAsync(ct))
                {
                    if (ct.IsCancellationRequested) break;
                    await ProcessMessageAsync(msg, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Thread cancelled gracefully.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Message processing error: {ex.Message}");
            }
        }

        // 停止所有线程
        public void Shutdown()
        {
            _cts.Cancel();
            lock (_lock)
            {
                foreach (var task in _activeTasks)
                {
                    task.Wait();
                }
                _activeTasks.Clear();
                _currentThreadCount = 0;
            }
        }
    }
}
