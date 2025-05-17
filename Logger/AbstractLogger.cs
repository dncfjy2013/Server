//using Server.Logger.Common;
//using System.Collections.Concurrent;
//using System.Runtime.CompilerServices;

//namespace Server.Logger
//{
//    public abstract class AbstractLogger : ILogger
//    {
//        protected readonly LoggerConfig _config;
//        protected readonly ConcurrentDictionary<string, LogTemplate> _templates = new();
//        protected readonly CancellationTokenSource _cts = new CancellationTokenSource();
//        protected bool _isDisposed;

//        public LogLevel ConsoleLogLevel
//        {
//            get => _config.ConsoleLogLevel;
//            set => _config.ConsoleLogLevel = value;
//        }

//        public LogLevel FileLogLevel
//        {
//            get => _config.FileLogLevel;
//            set => _config.FileLogLevel = value;
//        }

//        public string LogFilePath
//        {
//            get => _config.LogFilePath;
//            set => _config.LogFilePath = value;
//        }

//        public bool EnableAsyncWriting
//        {
//            get => _config.EnableAsyncWriting;
//            set => _config.EnableAsyncWriting = value;
//        }

//        protected AbstractLogger(LoggerConfig config)
//        {
//            _config = config ?? throw new ArgumentNullException(nameof(config));

//            // 添加默认模板
//            AddTemplate(new LogTemplate
//            {
//                Name = "Default",
//                Template = "{Timestamp} [{Level}] {Message}",
//                Level = LogLevel.Information,
//                IncludeException = true,
//                IncludeCallerInfo = false
//            });
//        }

//        // 模板管理实现
//        public virtual void AddTemplate(LogTemplate template)
//        {
//            if (template == null)
//                throw new ArgumentNullException(nameof(template));

//            _templates[template.Name] = template;
//        }

//        public virtual void RemoveTemplate(string templateName)
//        {
//            if (string.IsNullOrEmpty(templateName))
//                return;

//            _templates.TryRemove(templateName, out _);
//        }

//        public virtual LogTemplate GetTemplate(string templateName)
//        {
//            if (string.IsNullOrEmpty(templateName))
//                templateName = "Default";

//            if (_templates.TryGetValue(templateName, out var template))
//                return template;

//            return _templates["Default"];
//        }

//        // 基础日志方法实现
//        public virtual void Log(LogLevel level, string message, Exception exception = null,
//            Dictionary<string, object> properties = null, string templateName = null,
//            [CallerMemberName] string memberName = "",
//            [CallerFilePath] string sourceFilePath = "",
//            [CallerLineNumber] int sourceLineNumber = 0)
//        {
//            if (_isDisposed)
//                return;

//            var template = GetTemplate(templateName);

//            // 检查日志级别
//            if (level < template.Level)
//                return;

//            // 构建日志内容
//            var logEvent = new Dictionary<string, object>
//            {
//                ["Timestamp"] = DateTimeOffset.Now,
//                ["Level"] = level,
//                ["Message"] = message,
//                ["Exception"] = exception,
//                ["MemberName"] = memberName,
//                ["FilePath"] = sourceFilePath,
//                ["LineNumber"] = sourceLineNumber
//            };

//            if (properties != null)
//            {
//                foreach (var prop in properties)
//                {
//                    logEvent[prop.Key] = prop.Value;
//                }
//            }

//            // 格式化并输出日志
//            var formattedMessage = FormatMessage(template, logEvent);

//            // 根据配置决定输出位置
//            if (level >= ConsoleLogLevel)
//            {
//                WriteToConsole(level, formattedMessage, exception);
//            }

//            if (level >= FileLogLevel && !string.IsNullOrEmpty(LogFilePath))
//            {
//                if (EnableAsyncWriting)
//                {
//                    WriteToFileAsync(formattedMessage, exception).ConfigureAwait(false);
//                }
//                else
//                {
//                    WriteToFile(formattedMessage, exception);
//                }
//            }
//        }

//        // 快捷日志方法实现
//        public virtual void LogTrace(string message, Dictionary<string, object> properties = null, string templateName = null)
//            => Log(LogLevel.Trace, message, null, properties, templateName);

//        public virtual void LogDebug(string message, Dictionary<string, object> properties = null, string templateName = null)
//            => Log(LogLevel.Debug, message, null, properties, templateName);

//        public virtual void LogInformation(string message, Dictionary<string, object> properties = null, string templateName = null)
//            => Log(LogLevel.Information, message, null, properties, templateName);

//        public virtual void LogWarning(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null)
//            => Log(LogLevel.Warning, message, exception, properties, templateName);

//        public virtual void LogError(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null)
//            => Log(LogLevel.Error, message, exception, properties, templateName);

//        public virtual void LogCritical(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null)
//            => Log(LogLevel.Critical, message, exception, properties, templateName);

//        // 结构化日志方法实现
//        public virtual void LogTrace<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
//            => Log(LogLevel.Trace, FormatStructuredMessage(state, null, formatter), null, null, templateName);

//        public virtual void LogDebug<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
//            => Log(LogLevel.Debug, FormatStructuredMessage(state, null, formatter), null, null, templateName);

//        public virtual void LogInformation<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
//            => Log(LogLevel.Information, FormatStructuredMessage(state, null, formatter), null, null, templateName);

//        public virtual void LogWarning<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
//            => Log(LogLevel.Warning, FormatStructuredMessage(state, exception, formatter), exception, null, templateName);

//        public virtual void LogError<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
//            => Log(LogLevel.Error, FormatStructuredMessage(state, exception, formatter), exception, null, templateName);

//        public virtual void LogCritical<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
//            => Log(LogLevel.Critical, FormatStructuredMessage(state, exception, formatter), exception, null, templateName);

//        // 辅助方法：格式化结构化消息
//        protected virtual string FormatStructuredMessage<T>(T state, Exception exception, Func<T, Exception, string> formatter)
//        {
//            if (formatter != null)
//                return formatter(state, exception);

//            if (state is string str)
//                return str;

//            return state?.ToString() ?? "";
//        }

//        // 辅助方法：应用模板格式化消息
//        protected virtual string FormatMessage(LogTemplate template, Dictionary<string, object> values)
//        {
//            var message = template.Template;

//            foreach (var item in values)
//            {
//                if (item.Value != null)
//                {
//                    message = message.Replace($"{{{item.Key}}}", item.Value.ToString());
//                }
//            }

//            return message;
//        }

//        // 抽象方法 - 由具体实现类实现
//        protected abstract void WriteToConsole(LogLevel level, string message, Exception exception);
//        protected abstract void WriteToFile(string message, Exception exception);
//        protected abstract Task WriteToFileAsync(string message, Exception exception);

//        // 实现IDisposable
//        public virtual void Dispose()
//        {
//            if (_isDisposed)
//                return;

//            _cts.Cancel();
//            _cts.Dispose();
//            _isDisposed = true;

//            GC.SuppressFinalize(this);
//        }
//    }
//}