using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Common
{
    /// <summary>
    /// 线程安全、高性能、支持超时/审计/监控的通用状态机
    /// </summary>
    /// <typeparam name="TKey">状态唯一键类型</typeparam>
    /// <typeparam name="TState">状态枚举/类型</typeparam>
    public class StateMachine<TKey, TState> : IDisposable where TState : notnull
    {
        #region 核心数据结构
        private readonly ConcurrentDictionary<TKey, StateContext> _states = new();
        private readonly ConcurrentDictionary<TKey, object> _keyLocks = new();
        private readonly Dictionary<TState, HashSet<TState>> _transitions = new();
        private readonly ConcurrentQueue<AuditLogEntry> _auditLog = new();
        private readonly object _timeoutQueueLock = new();
        #endregion

        #region 性能计数器
        private long _totalTransitions;
        private long _successfulTransitions;
        private long _failedTransitions;
        private readonly Meter _meter = new("StateMachine");
        private readonly Histogram<double> _transitionLatency;
        #endregion

        #region 状态历史配置
        private readonly int _maxHistoryPerKey = 100;
        private readonly Timer _timeoutScanner;
        private readonly PriorityQueue<TimeoutTask, long> _timeoutQueue = new();
        private readonly ConcurrentDictionary<TKey, bool> _scheduledTimeouts = new(); // 修复：超时去重
        #endregion

        public StateMachine()
        {
            _transitionLatency = _meter.CreateHistogram<double>("transition_latency_ms");
            _meter.CreateObservableCounter("total_transitions", () => _totalTransitions);
            _meter.CreateObservableCounter("successful_transitions", () => _successfulTransitions);
            _meter.CreateObservableCounter("failed_transitions", () => _failedTransitions);

            // 启动超时扫描器
            _timeoutScanner = new Timer(CheckTimeouts, null, 1000, 1000);
        }

        public record struct StateHistoryEntry(TState State, DateTimeOffset Timestamp, string? Reason);
        private record struct TimeoutTask(TKey Key, DateTimeOffset ExpireTime);

        #region 公共接口
        /// <summary>
        /// 初始化键的初始状态
        /// </summary>
        public void InitializeState(TKey key, TState initialState)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            _states.TryAdd(key, new StateContext
            {
                CurrentState = initialState,
                LastUpdated = DateTimeOffset.UtcNow
            });
        }

        /// <summary>
        /// 添加允许的状态转换规则
        /// </summary>
        public void AddTransition(TState from, TState to)
        {
            if (from == null || to == null) throw new ArgumentNullException();

            lock (_transitions)
            {
                if (!_transitions.TryGetValue(from, out var set))
                {
                    set = new HashSet<TState>();
                    _transitions[from] = set;
                }
                set.Add(to);
            }
        }

        /// <summary>
        /// 异步执行状态转换（核心方法）
        /// </summary>
        public async Task<bool> TransitionAsync(
            TKey key,
            TState toState,
            Func<TKey, TState, TState, Task>? transitionAction = null,
            string? reason = null)
        {
            var startTime = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref _totalTransitions);

            try
            {
                var keyLock = _keyLocks.GetOrAdd(key, _ => new object());
                bool result = await Task.Run(async () =>
                {
                    // 第一段：锁内校验（原子操作）
                    if (!_states.TryGetValue(key, out var originalContext))
                        return false;

                    var originalState = originalContext.CurrentState;
                    if (!IsTransitionAllowed(originalState, toState))
                        return false;

                    OnBeforeTransition?.Invoke(key, originalState, toState);

                    // 第二段：锁外执行异步业务（避免锁阻塞）
                    Exception? actionError = null;
                    if (transitionAction != null)
                    {
                        try
                        {
                            await transitionAction(key, originalState, toState);
                        }
                        catch (Exception ex)
                        {
                            actionError = ex;
                        }
                    }

                    // 第三段：锁内提交状态（防并发修改）
                    lock (keyLock)
                    {
                        // 再次校验：防止期间状态被修改
                        if (!_states.TryGetValue(key, out var currentContext) ||
                            !currentContext.CurrentState.Equals(originalState))
                            return false;

                        if (actionError != null)
                            throw new InvalidOperationException("状态转换业务执行失败", actionError);

                        // 修复：创建新上下文，避免竞态
                        var newContext = currentContext.Clone();
                        RecordHistory(newContext, toState, reason);
                        UpdateStateContext(key, newContext, toState);

                        // 替换原数据
                        _states.TryUpdate(key, newContext, currentContext);

                        RecordAudit(key, originalState, toState, true, null);
                        OnAfterTransition?.Invoke(key, originalState, toState);
                        return true;
                    }
                });

                if (result) Interlocked.Increment(ref _successfulTransitions);
                return result;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedTransitions);
                _states.TryGetValue(key, out var ctx);

                TState currentState = ctx != null ? ctx.CurrentState : default!;
                RecordAudit(key, currentState, toState, false, ex);
                OnTransitionFailed?.Invoke(key, currentState, toState, ex);
                return false;
            }
            finally
            {
                // 记录转换延迟
                var elapsed = (Stopwatch.GetTimestamp() - startTime) * 1000.0 / Stopwatch.Frequency;
                _transitionLatency.Record(elapsed);
            }
        }

        /// <summary>
        /// 为状态设置超时自动回退
        /// </summary>
        public void SetTimeout(TKey key, TimeSpan timeout, TState fallbackState)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (fallbackState == null) throw new ArgumentNullException(nameof(fallbackState));

            var keyLock = _keyLocks.GetOrAdd(key, _ => new object());
            lock (keyLock)
            {
                if (!_states.TryGetValue(key, out var context))
                    throw new KeyNotFoundException("键未初始化");

                var newContext = context.Clone();
                newContext.Timeout = timeout;
                newContext.FallbackState = fallbackState;
                _states.TryUpdate(key, newContext, context);

                ScheduleTimeoutCheck(key, timeout);
            }
        }

        /// <summary>
        /// 获取状态变更历史
        /// </summary>
        public IEnumerable<StateHistoryEntry> GetStateHistory(TKey key)
        {
            return _states.TryGetValue(key, out var context)
                ? context.History.ToList()
                : Enumerable.Empty<StateHistoryEntry>();
        }

        /// <summary>
        /// 获取审计日志
        /// </summary>
        public IEnumerable<AuditLogEntry> GetAuditLogs()
        {
            return _auditLog.ToArray();
        }

        /// <summary>
        /// 尝试获取当前状态
        /// </summary>
        public bool TryGetCurrentState(TKey key, out TState state)
        {
            if (_states.TryGetValue(key, out var context))
            {
                state = context.CurrentState;
                return true;
            }

            state = default!;
            return false;
        }

        /// <summary>
        /// 移除指定键的状态（清理资源）
        /// </summary>
        public bool RemoveState(TKey key)
        {
            _states.TryRemove(key, out _);
            _keyLocks.TryRemove(key, out _);
            _scheduledTimeouts.TryRemove(key, out _);
            return true;
        }
        #endregion

        #region 私有方法
        private bool IsTransitionAllowed(TState fromState, TState toState)
        {
            lock (_transitions)
            {
                return _transitions.TryGetValue(fromState, out var allowed) && allowed.Contains(toState);
            }
        }

        private void RecordHistory(StateContext context, TState newState, string? reason)
        {
            context.History.AddLast(new StateHistoryEntry(newState, DateTimeOffset.UtcNow, reason));
            while (context.History.Count > _maxHistoryPerKey)
                context.History.RemoveFirst();
        }

        private void UpdateStateContext(TKey key, StateContext context, TState newState)
        {
            context.CurrentState = newState;
            context.LastUpdated = DateTimeOffset.UtcNow;

            // 重新调度超时
            if (context.Timeout.HasValue)
                ScheduleTimeoutCheck(key, context.Timeout.Value);
        }

        /// <summary>
        /// 修复：超时任务去重，避免重复添加
        /// </summary>
        private void ScheduleTimeoutCheck(TKey key, TimeSpan timeout)
        {
            if (_scheduledTimeouts.TryGetValue(key, out _))
                return;

            var expireTime = DateTimeOffset.UtcNow + timeout;
            lock (_timeoutQueueLock)
            {
                _timeoutQueue.Enqueue(new TimeoutTask(key, expireTime), expireTime.Ticks);
                _scheduledTimeouts.TryAdd(key, true);
            }
        }

        /// <summary>
        /// 定时扫描超时任务
        /// </summary>
        private void CheckTimeouts(object? state)
        {
            var now = DateTimeOffset.UtcNow;
            while (true)
            {
                TimeoutTask task;
                long priority;

                lock (_timeoutQueueLock)
                {
                    if (!_timeoutQueue.TryPeek(out task, out priority)) break;
                    if (priority > now.Ticks) break;
                    _timeoutQueue.Dequeue();
                }

                _scheduledTimeouts.TryRemove(task.Key, out _);
                _ = HandleTimeoutAsync(task.Key); // 安全调用异步方法
            }
        }

        /// <summary>
        /// 修复：async void 改为 async Task，避免吞异常
        /// </summary>
        private async Task HandleTimeoutAsync(TKey key)
        {
            try
            {
                if (!TryGetCurrentState(key, out _)) return;

                var keyLock = _keyLocks.GetOrAdd(key, _ => new object());
                TState? fallback = default;

                lock (keyLock)
                {
                    if (!_states.TryGetValue(key, out var ctx) || ctx.FallbackState is null) return;
                    if (DateTimeOffset.UtcNow < ctx.LastUpdated + ctx.Timeout) return;
                    fallback = ctx.FallbackState;
                }

                if (fallback is not null)
                {
                    await TransitionAsync(key, fallback, reason: "状态超时自动回退");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"超时处理异常 [{key}]: {ex}");
            }
        }

        /// <summary>
        /// 记录审计日志
        /// </summary>
        private void RecordAudit(TKey key, TState from, TState to, bool success, Exception? ex)
        {
            var entry = new AuditLogEntry(
                DateTimeOffset.UtcNow,
                key?.ToString() ?? "null",
                from?.ToString() ?? "null",
                to?.ToString() ?? "null",
                success,
                ex?.Message
            );

            _auditLog.Enqueue(entry);
            // 限制日志最大数量
            while (_auditLog.Count > 10000)
                _auditLog.TryDequeue(out _);
        }
        #endregion

        #region 事件 & 内部类
        public event Action<TKey, TState, TState>? OnBeforeTransition;
        public event Action<TKey, TState, TState>? OnAfterTransition;
        public event Action<TKey, TState, TState, Exception>? OnTransitionFailed;

        public sealed record AuditLogEntry(
            DateTimeOffset Timestamp,
            string Key,
            string FromState,
            string ToState,
            bool Success,
            string? Error
        );

        /// <summary>
        /// 修复：增加Clone方法，杜绝引用竞态
        /// </summary>
        private sealed class StateContext
        {
            public TState CurrentState { get; set; } = default!;
            public LinkedList<StateHistoryEntry> History { get; init; } = new();
            public DateTimeOffset LastUpdated { get; set; }
            public TimeSpan? Timeout { get; set; }
            public TState? FallbackState { get; set; }

            public StateContext Clone()
            {
                var clone = new StateContext
                {
                    CurrentState = CurrentState,
                    LastUpdated = LastUpdated,
                    Timeout = Timeout,
                    FallbackState = FallbackState
                };
                foreach (var item in History) clone.History.AddLast(item);
                return clone;
            }
        }
        #endregion

        #region 释放资源
        public void Dispose()
        {
            _timeoutScanner?.Dispose();
            _meter.Dispose();
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}