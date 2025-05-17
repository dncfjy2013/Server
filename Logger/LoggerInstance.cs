using Server.Logger.Common;
using System;
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

        public LogMessage(DateTime timestamp, LogLevel level, string message, int threadId, string threadName, Exception exception = null, Dictionary<string, object> properties = null)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
            ThreadId = threadId;
            ThreadName = threadName;
            Exception = exception;
            Properties = properties;
        }

        public string LevelMessage => Level.ToString().ToUpperInvariant();
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

    public class LoggerInstance : ILogger
    {
        // 单例实现
        private static readonly Lazy<LoggerInstance> _instance = new Lazy<LoggerInstance>(
            () => new LoggerInstance(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static LoggerInstance Instance => _instance.Value;

        // 配置和状态
        private readonly LoggerConfig _config;
        private readonly ConcurrentDictionary<string, LogTemplate> _templates = new();
        private readonly BlockingCollection<LogMessage> _logQueue = new(10000);
        private readonly Task _logWriterTask;
        private readonly CancellationTokenSource _cts = new();
        private bool _isDisposed;
        private bool _isDisposing;

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
            set => _config.LogFilePath = value;
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

            // 添加默认模板
            AddTemplate(new LogTemplate
            {
                Name = "Default",
                Template = "{Timestamp} [{Level}] {Message}{Exception}{Properties}",
                Level = LogLevel.Information,
                IncludeException = true
            });

            // 启动异步日志处理任务
            if (_config.EnableAsyncWriting)
            {
                _logWriterTask = Task.Factory.StartNew(
                    ProcessLogQueue,
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
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

            // 创建日志消息
            var logMessage = new LogMessage(
                DateTime.Now,
                level,
                message,
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.Name,
                exception,
                properties)
            {
                // 添加调用者信息
                Properties = properties ?? new Dictionary<string, object>
                {
                    ["CallerMember"] = memberName,
                    ["CallerFile"] = Path.GetFileName(sourceFilePath),
                    ["CallerLine"] = sourceLineNumber
                }
            };

            // 格式化消息
            var formattedMessage = FormatMessage(template, logMessage);

            // 输出到控制台
            if (level >= ConsoleLogLevel)
            {
                WriteToConsole(level, formattedMessage);
            }

            // 输出到文件
            if (level >= FileLogLevel && !string.IsNullOrEmpty(LogFilePath))
            {
                if (EnableAsyncWriting)
                {
                    _logQueue.Add(logMessage, _cts.Token);
                }
                else
                {
                    WriteToFile(logMessage);
                }
            }
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
        private string FormatMessage(LogTemplate template, LogMessage message)
        {
            var formatted = template.Template
                .Replace("{Timestamp}", message.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Replace("{Level}", message.LevelMessage)
                .Replace("{Message}", message.Message);

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

        // 写入控制台
        private void WriteToConsole(LogLevel level, string message)
        {
            if (_isDisposing) return;

            var color = GetConsoleColor(level);
            var originalColor = Console.ForegroundColor;

            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        // 写入文件
        private void WriteToFile(LogMessage message)
        {
            if (_isDisposing) return;

            int retryCount = 0;
            const int maxRetries = 3;

            while (retryCount < maxRetries)
            {
                try
                {
                    using var fs = new FileStream(
                        LogFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read,
                        4096,
                        useAsync: false);

                    using var writer = new StreamWriter(fs, Encoding.UTF8);
                    writer.WriteLine(FormatMessage(GetTemplate("Default"), message));
                    break;
                }
                catch (IOException ex) when (ex.HResult == -2147024864) // 文件被占用
                {
                    retryCount++;
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    WriteToConsole(LogLevel.Error, $"文件写入失败: {ex.Message}");
                    break;
                }
            }

            if (retryCount >= maxRetries)
            {
                WriteToConsole(LogLevel.Critical, "日志写入达到最大重试次数");
            }
        }

        // 异步处理日志队列
        private void ProcessLogQueue()
        {
            try
            {
                foreach (var message in _logQueue.GetConsumingEnumerable(_cts.Token))
                {
                    WriteToFile(message);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常终止
            }
            finally
            {
                // 处理队列中剩余的消息
                while (_logQueue.TryTake(out var message))
                {
                    WriteToFile(message);
                }
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

        // 资源释放
        public void Dispose()
        {
            if (_isDisposed || _isDisposing) return;
            _isDisposing = true;

            try
            {
                _cts.Cancel();
                _logQueue.CompleteAdding();

                // 等待异步任务完成
                if (_logWriterTask != null && !_logWriterTask.IsCompleted)
                {
                    _logWriterTask.Wait(TimeSpan.FromSeconds(5));
                }

                // 处理剩余消息
                while (_logQueue.TryTake(out var message))
                {
                    WriteToFile(message);
                }
            }
            catch (Exception ex)
            {
                WriteToConsole(LogLevel.Critical, $"日志系统关闭异常: {ex.Message}");
            }
            finally
            {
                _logQueue.Dispose();
                _cts.Dispose();
                _isDisposed = true;
                _isDisposing = false;
            }
        }
    }
    
}