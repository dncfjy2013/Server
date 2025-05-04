using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public class StateMachine<TKey, TState> where TState : notnull
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
    #endregion

    public StateMachine()
    {
        _transitionLatency = _meter.CreateHistogram<double>("transition_latency_ms");
        _meter.CreateObservableCounter("total_transitions", () => _totalTransitions);
        _timeoutScanner = new Timer(CheckTimeouts, null, 1000, 1000);
    }

    public record struct StateHistoryEntry(TState State, DateTimeOffset Timestamp, string? Reason);
    private record struct TimeoutTask(TKey Key, DateTimeOffset ExpireTime);

    #region 公共接口
    public void InitializeState(TKey key, TState initialState)
    {
        _states.TryAdd(key, new StateContext
        {
            CurrentState = initialState,
            LastUpdated = DateTimeOffset.UtcNow
        });
    }

    public void AddTransition(TState from, TState to)
    {
        if (!_transitions.TryGetValue(from, out var set))
        {
            set = new HashSet<TState>();
            _transitions[from] = set;
        }
        set.Add(to);
    }

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
                StateContext context;
                TState originalState;

                // 第一阶段：验证和前置操作
                lock (keyLock)
                {
                    if (!_states.TryGetValue(key, out context))
                        return false;

                    originalState = context.CurrentState;
                    if (!IsTransitionAllowed(originalState, toState))
                        return false;

                    OnBeforeTransition?.Invoke(key, originalState, toState);
                }

                // 异步操作在锁外执行
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

                // 第二阶段：状态提交
                lock (keyLock)
                {
                    if (!_states.TryGetValue(key, out context) ||
                        !context.CurrentState.Equals(originalState))
                    {
                        return false; // 状态已变更
                    }

                    if (actionError != null)
                    {
                        throw new InvalidOperationException("Transition action failed", actionError);
                    }

                    RecordHistory(context, toState, reason);
                    UpdateStateContext(key, context, toState);
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
            _states.TryGetValue(key, out var context);
            RecordAudit(key, context.CurrentState ?? default!, toState, false, ex);
            OnTransitionFailed?.Invoke(key, context.CurrentState ?? default!, toState, ex);
            return false;
        }
        finally
        {
            var elapsed = (Stopwatch.GetTimestamp() - startTime) * 1000.0 / Stopwatch.Frequency;
            _transitionLatency.Record(elapsed);
        }
    }

    public void SetTimeout(TKey key, TimeSpan timeout, TState fallbackState)
    {
        lock (_keyLocks.GetOrAdd(key, _ => new object()))
        {
            if (!_states.TryGetValue(key, out var context))
                throw new KeyNotFoundException();

            context.Timeout = timeout;
            context.FallbackState = fallbackState;
            ScheduleTimeoutCheck(key, timeout);
        }
    }

    public IEnumerable<StateHistoryEntry> GetStateHistory(TKey key)
    {
        return _states.TryGetValue(key, out var context)
            ? new List<StateHistoryEntry>(context.History)
            : Enumerable.Empty<StateHistoryEntry>();
    }

    public IEnumerable<AuditLogEntry> GetAuditLogs()
    {
        return _auditLog.ToArray().AsEnumerable();
    }

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

    public void Dispose()
    {
        _timeoutScanner?.Dispose();
        _meter.Dispose();
    }
    #endregion

    #region 私有方法
    private bool IsTransitionAllowed(TState fromState, TState toState)
    {
        return _transitions.TryGetValue(fromState, out var allowedStates)
               && allowedStates.Contains(toState);
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

        if (context.Timeout.HasValue)
        {
            ScheduleTimeoutCheck(key, context.Timeout.Value);
        }
    }

    private void ScheduleTimeoutCheck(TKey key, TimeSpan timeout)
    {
        var expireTime = DateTimeOffset.UtcNow + timeout;
        lock (_timeoutQueueLock)
        {
            _timeoutQueue.Enqueue(new TimeoutTask(key, expireTime), expireTime.Ticks);
        }
    }

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

            HandleTimeout(task.Key);
        }
    }

    private async void HandleTimeout(TKey key)
    {
        try
        {
            TState? fallbackState = default;
            lock (_keyLocks.GetOrAdd(key, _ => new object()))
            {
                if (!_states.TryGetValue(key, out var context)) return;
                if (context.FallbackState == null) return;
                if (DateTimeOffset.UtcNow < context.LastUpdated + context.Timeout) return;

                fallbackState = context.FallbackState;
            }

            if (fallbackState != null)
            {
                await TransitionAsync(key, fallbackState, reason: "State timeout");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Timeout handler error: {ex}");
        }
    }

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
        while (_auditLog.Count > 10000)
            _auditLog.TryDequeue(out _);
    }
    #endregion

    #region 事件和类型
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
    #endregion

    private sealed class StateContext
    {
        public TState CurrentState { get; set; } = default!;
        public LinkedList<StateHistoryEntry> History { get; } = new();
        public DateTimeOffset LastUpdated { get; set; }
        public TimeSpan? Timeout { get; set; }
        public TState? FallbackState { get; set; }
    }
}