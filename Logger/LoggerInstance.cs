using Server.Common.Extensions;
using Server.Logger.Common;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Logger.Common
{
    // 日志级别枚举
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Information = 2,
        Warning = 3,
        Error = 4,
        Critical = 5
    }

    // 日志配置类
    public class LoggerConfig
    {
        public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Information;
        public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
        public string LogFilePath { get; set; } = "application.log";
        public bool EnableAsyncWriting { get; set; } = true;
        public int MaxQueueSize { get; set; } = 100000;
        public int BatchSize { get; set; } = 1000;
        public int FlushInterval { get; set; } = 100;
        public bool EnableConsoleColor { get; set; } = true; // 新增：控制台颜色开关
    }

    // 日志消息类
    public class LogMessage
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public int ThreadId { get; set; }
        public string ThreadName { get; set; }
        public Exception Exception { get; set; }
        public Dictionary<string, object> Properties { get; set; }

        public LogMessage() { }

        public LogMessage(DateTime timestamp, LogLevel level, string message, int threadId, string threadName,
            Exception exception = null, Dictionary<string, object> properties = null)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
            ThreadId = threadId;
            ThreadName = threadName;
            Exception = exception;
            Properties = properties;
        }

        public void Reset()
        {
            Timestamp = DateTime.MinValue;
            Level = LogLevel.Information;
            Message = null;
            ThreadId = 0;
            ThreadName = null;
            Exception = null;
            Properties?.Clear();
        }

        public string LevelMessage => Level.ToString().ToUpperInvariant().Center(11, " ");
    }

    // 对象池实现
    public class ObjectPool<T> where T : class, new()
    {
        private readonly ConcurrentBag<T> _objects;
        private readonly Func<T> _objectGenerator;

        public ObjectPool(Func<T> objectGenerator)
        {
            _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
            _objects = new ConcurrentBag<T>();
        }

        public T Get() => _objects.TryTake(out T item) ? item : _objectGenerator();

        public void Return(T item) => _objects.Add(item);
    }
}

namespace Server.Logger
{
    // 日志模板配置
    public class LogTemplate
    {
        public string Name { get; set; }
        public string Template { get; set; }
        public LogLevel Level { get; set; }
        public bool IncludeException { get; set; }
        public bool IncludeCallerInfo { get; set; }
    }

    // 日志接口
    public interface ILogger : IDisposable
    {
        LogLevel ConsoleLogLevel { get; set; }
        LogLevel FileLogLevel { get; set; }
        string LogFilePath { get; set; }
        bool EnableAsyncWriting { get; set; }

        // 模板管理
        void AddTemplate(LogTemplate template);
        void RemoveTemplate(string templateName);
        LogTemplate GetTemplate(string templateName);

        // 基础日志方法
        void Log(LogLevel level, string message, Exception exception = null,
            Dictionary<string, object> properties = null, string templateName = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0);

        // 快捷日志方法 - 无异常
        void LogTrace(string message, Dictionary<string, object> properties = null, string templateName = null);
        void LogDebug(string message, Dictionary<string, object> properties = null, string templateName = null);
        void LogInformation(string message, Dictionary<string, object> properties = null, string templateName = null);

        // 快捷日志方法 - 带异常
        void LogWarning(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);
        void LogError(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);
        void LogCritical(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);

        // 结构化日志方法
        void LogTrace<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogDebug<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogInformation<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogWarning<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogError<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogCritical<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
    }

    // 高性能日志实现
    public sealed class LoggerInstance : ILogger
    {
        // 单例实现
        private static readonly Lazy<LoggerInstance> _instance = new Lazy<LoggerInstance>(
            () => new LoggerInstance(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static LoggerInstance Instance => _instance.Value;

        // 配置和状态
        private readonly LoggerConfig _config;
        private readonly ConcurrentDictionary<string, LogTemplate> _templates = new();
        private readonly ConcurrentQueue<LogMessage> _logQueue = new();
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _logWriterTask;
        private FileStream _fileStream;
        private StreamWriter _writer;
        private int _bufferCount;
        private bool _isDisposed;
        private bool _isDisposing;

        // 线程本地缓存原始颜色（避免多线程竞争）
        [ThreadStatic] private static ConsoleColor _originalColor;
        [ThreadStatic] private static bool _colorInitialized;

        // 接口属性实现
        public LogLevel ConsoleLogLevel
        {
            get => _config.ConsoleLogLevel;
            set => _config.ConsoleLogLevel = value;
        }

        public LogLevel FileLogLevel
        {
            get => _config.FileLogLevel;
            set => _config.FileLogLevel = value;
        }

        public string LogFilePath
        {
            get => _config.LogFilePath;
            set
            {
                if (_config.LogFilePath != value)
                {
                    _config.LogFilePath = value;
                    ResetFileWriter();
                }
            }
        }

        public bool EnableAsyncWriting
        {
            get => _config.EnableAsyncWriting;
            set => _config.EnableAsyncWriting = value;
        }

        // 构造函数
        private LoggerInstance() : this(new LoggerConfig()) { }

        public LoggerInstance(LoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _messagePool = new ObjectPool<LogMessage>(() => new LogMessage());

            // 添加默认模板
            AddTemplate(new LogTemplate
            {
                Name = "Default",
                Template = "{Timestamp} [{Level}] [{ThreadId}] {Message}{Exception}{Properties}",
                Level = LogLevel.Information,
                IncludeException = true
            });

            InitializeFileWriter();

            // 启动异步日志处理任务
            _logWriterTask = Task.Factory.StartNew(
                ProcessLogQueueAsync,
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        // 初始化文件写入器
        private void InitializeFileWriter()
        {
            try
            {
                _fileStream?.Dispose();
                _writer?.Dispose();

                _fileStream = new FileStream(
                    _config.LogFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    4096,
                    useAsync: true);

                _writer = new StreamWriter(_fileStream, Encoding.UTF8) { AutoFlush = false };
                _bufferCount = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化日志文件失败: {ex.Message}");
                throw;
            }
        }

        // 重置文件写入器
        private void ResetFileWriter()
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
                _fileStream?.Dispose();

                InitializeFileWriter();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重置日志文件失败: {ex.Message}");
            }
        }

        // 模板管理方法
        public void AddTemplate(LogTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            _templates[template.Name] = template;
        }

        public void RemoveTemplate(string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return;
            _templates.TryRemove(templateName, out _);
        }

        public LogTemplate GetTemplate(string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) templateName = "Default";
            return _templates.TryGetValue(templateName, out var template)
                ? template
                : _templates["Default"];
        }

        // 基础日志方法
        public void Log(
            LogLevel level,
            string message,
            Exception exception = null,
            Dictionary<string, object> properties = null,
            string templateName = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            if (_isDisposed) return;

            var template = GetTemplate(templateName);

            // 检查日志级别过滤
            if (level < template.Level) return;

            // 获取对象池中的对象
            var logMessage = _messagePool.Get();
            logMessage.Timestamp = DateTime.Now;
            logMessage.Level = level;
            logMessage.Message = message;
            logMessage.ThreadId = Environment.CurrentManagedThreadId;
            logMessage.ThreadName = Thread.CurrentThread.Name;
            logMessage.Exception = exception;
            logMessage.Properties = properties ?? new Dictionary<string, object>
            {
                ["CallerMember"] = memberName,
                ["CallerFile"] = Path.GetFileName(sourceFilePath),
                ["CallerLine"] = sourceLineNumber
            };

            // 加入队列（应用背压策略）
            EnqueueLogMessage(logMessage);
        }

        // 快捷日志方法实现
        public void LogTrace(string message, Dictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Trace, message, null, properties, templateName);

        public void LogDebug(string message, Dictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Debug, message, null, properties, templateName);

        public void LogInformation(string message, Dictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Information, message, null, properties, templateName);

        public void LogWarning(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Warning, message, exception, properties, templateName);

        public void LogError(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Error, message, exception, properties, templateName);

        public void LogCritical(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Critical, message, exception, properties, templateName);

        // 结构化日志方法实现
        public void LogTrace<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Trace, FormatStructuredMessage(state, null, formatter), null, null, templateName);

        public void LogDebug<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Debug, FormatStructuredMessage(state, null, formatter), null, null, templateName);

        public void LogInformation<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Information, FormatStructuredMessage(state, null, formatter), null, null, templateName);

        public void LogWarning<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Warning, FormatStructuredMessage(state, exception, formatter), exception, null, templateName);

        public void LogError<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Error, FormatStructuredMessage(state, exception, formatter), exception, null, templateName);

        public void LogCritical<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Critical, FormatStructuredMessage(state, exception, formatter), exception, null, templateName);

        // 格式化结构化消息
        private string FormatStructuredMessage<T>(T state, Exception exception, Func<T, Exception, string> formatter)
        {
            if (formatter != null)
                return formatter(state, exception);

            if (state is string str)
                return str;

            return state?.ToString() ?? "";
        }

        // 格式化消息
        private string FormatMessage(LogMessage message, string templateName = "Default")
        {
            var template = GetTemplate(templateName);
            var formatted = template.Template
                .Replace("{Timestamp}", message.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Replace("{Level}", message.LevelMessage)
                .Replace("{Message}", message.Message)
                .Replace("{ThreadId}", message.ThreadId.ToString());

            // 处理异常信息
            if (template.IncludeException && message.Exception != null)
            {
                var exceptionInfo = $"[Exception] {message.Exception.GetType().Name}: {message.Exception.Message}";
                if (!string.IsNullOrEmpty(message.Exception.StackTrace))
                {
                    exceptionInfo += $"\n{message.Exception.StackTrace}";
                }
                formatted = formatted.Replace("{Exception}", exceptionInfo);
            }
            else
            {
                formatted = formatted.Replace("{Exception}", "");
            }

            // 处理属性信息
            if (message.Properties != null && message.Properties.Count > 0)
            {
                var propertiesInfo = string.Join(", ", message.Properties.Select(p => $"{p.Key}={p.Value}"));
                formatted = formatted.Replace("{Properties}", $"\n[Properties] {propertiesInfo}");
            }
            else
            {
                formatted = formatted.Replace("{Properties}", "");
            }

            return formatted;
        }

        // 加入日志队列（带背压策略）
        private void EnqueueLogMessage(LogMessage message)
        {
            // 应用背压策略
            if (_logQueue.Count > _config.MaxQueueSize)
            {
                // 队列已满，丢弃最旧的日志
                if (_logQueue.TryDequeue(out var discarded))
                {
                    _messagePool.Return(discarded);
                }

                // 记录队列满警告
                if (_logQueue.Count % 1000 == 0)
                {
                    var warningMsg = _messagePool.Get();
                    warningMsg.Timestamp = DateTime.Now;
                    warningMsg.Level = LogLevel.Warning;
                    warningMsg.Message = $"日志队列已满，当前大小: {_logQueue.Count}";
                    warningMsg.ThreadId = Environment.CurrentManagedThreadId;

                    // 直接写入控制台，避免递归调用
                    WriteToConsoleDirect(warningMsg);
                    _messagePool.Return(warningMsg);
                }
            }

            _logQueue.Enqueue(message);
            _semaphore.Release();
        }

        // 直接写入控制台（不经过队列）
        private void WriteToConsoleDirect(LogMessage message)
        {
            if (!_config.EnableConsoleColor)
            {
                Console.WriteLine(FormatMessage(message));
                return;
            }

            var color = GetConsoleColor(message.Level);

            // 缓存原始颜色（仅首次）
            if (!_colorInitialized)
            {
                _originalColor = Console.ForegroundColor;
                _colorInitialized = true;
            }

            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(FormatMessage(message));
            }
            finally
            {
                Console.ForegroundColor = _originalColor;
            }
        }

        // 写入控制台（优化版本）
        private void WriteToConsole(LogMessage message)
        {
            if (!_config.EnableConsoleColor)
            {
                Console.WriteLine(FormatMessage(message));
                return;
            }

            var color = GetConsoleColor(message.Level);

            // 缓存原始颜色（仅首次）
            if (!_colorInitialized)
            {
                _originalColor = Console.ForegroundColor;
                _colorInitialized = true;
            }

            try
            {
                Console.ForegroundColor = color;
                Console.Write(FormatMessage(message));
                Console.Write(Environment.NewLine); // 拆分WriteLine为Write+换行，减少锁竞争
            }
            finally
            {
                Console.ForegroundColor = _originalColor;
            }
        }

        // 异步处理日志队列
        private async Task ProcessLogQueueAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await _semaphore.WaitAsync(_cts.Token);

                    // 批量处理日志
                    var batchSize = Math.Min(_config.BatchSize, _logQueue.Count);
                    for (int i = 0; i < batchSize && _logQueue.TryDequeue(out var message); i++)
                    {
                        try
                        {
                            // 写入文件
                            if (message.Level >= FileLogLevel)
                            {
                                await WriteToFileAsync(message);
                            }

                            // 写入控制台（同步非阻塞，带颜色）
                            if (message.Level >= ConsoleLogLevel)
                            {
                                WriteToConsole(message);
                            }
                        }
                        finally
                        {
                            // 归还对象到池
                            _messagePool.Return(message);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常终止
            }
            catch (Exception ex)
            {
                Console.WriteLine($"日志处理任务异常: {ex}");
            }
            finally
            {
                // 处理队列中剩余的消息
                while (_logQueue.TryDequeue(out var message))
                {
                    try
                    {
                        if (message.Level >= FileLogLevel)
                        {
                            await WriteToFileAsync(message);
                        }
                    }
                    finally
                    {
                        _messagePool.Return(message);
                    }
                }

                // 确保所有缓冲区刷新
                await FlushWriterAsync();
            }
        }

        // 异步写入文件
        // 在WriteToFileAsync方法中修正日志写入失败的处理
        private async Task WriteToFileAsync(LogMessage message)
        {
            if (_isDisposing) return;

            try
            {
                var formatted = FormatMessage(message);
                await _writer.WriteLineAsync(formatted);
                _bufferCount++;

                // 批量刷新或定时刷新
                if (_bufferCount >= _config.FlushInterval)
                {
                    await _writer.FlushAsync();
                    _bufferCount = 0;
                }
            }
            catch (Exception ex)
            {
                // 记录写入失败 - 修正版本
                var errorMessage = _messagePool.Get();
                errorMessage.Timestamp = DateTime.Now;
                errorMessage.Level = LogLevel.Error;
                errorMessage.Message = $"日志写入失败: {ex.Message}";
                errorMessage.ThreadId = Environment.CurrentManagedThreadId;

                WriteToConsole(errorMessage);
                _messagePool.Return(errorMessage);

                // 尝试重置文件写入器
                ResetFileWriter();
            }
        }

        // 强制刷新写入器
        private async Task FlushWriterAsync()
        {
            try
            {
                if (_writer != null)
                {
                    await _writer.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"刷新日志缓冲区失败: {ex.Message}");
            }
        }

        // 获取控制台颜色
        private ConsoleColor GetConsoleColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Critical => ConsoleColor.Red,
                LogLevel.Error => ConsoleColor.DarkRed,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Information => ConsoleColor.Green,
                LogLevel.Debug => ConsoleColor.Cyan,
                LogLevel.Trace => ConsoleColor.Gray,
                _ => ConsoleColor.White,
            };
        }

        // 对象池引用
        private readonly ObjectPool<LogMessage> _messagePool;

        // 资源释放
        public void Dispose()
        {
            if (_isDisposed || _isDisposing) return;
            _isDisposing = true;

            try
            {
                _cts.Cancel();

                // 等待异步任务完成，设置超时
                Task.WaitAll(
                    new[] { _logWriterTask },
                    TimeSpan.FromSeconds(10));

                // 处理剩余消息
                while (_logQueue.TryDequeue(out var message))
                {
                    try
                    {
                        if (message.Level >= FileLogLevel)
                        {
                            _writer.WriteLine(FormatMessage(message));
                        }
                    }
                    finally
                    {
                        _messagePool.Return(message);
                    }
                }

                // 强制刷新所有缓冲区
                _writer?.Flush();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException))
            {
                // 忽略任务取消异常
            }
            catch (Exception ex)
            {
                Console.WriteLine($"日志系统关闭异常: {ex.Message}");
            }
            finally
            {
                // 释放资源
                _semaphore.Dispose();
                _writer?.Close();
                _fileStream?.Close();
                _cts.Dispose();
                _isDisposed = true;
                _isDisposing = false;
            }
        }
    }
}