using Logger._2ExpandVersion.Commmon;
using Microsoft.Win32.SafeHandles;
using Server.Logger.Common;
using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace Logger._2ExpandVersion
{
    // 高性能日志实现
    public sealed class LoggerInstance : ILogger
    {
        private static readonly Lazy<LoggerInstance> _instance = new Lazy<LoggerInstance>(
            () => new LoggerInstance(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static LoggerInstance Instance => _instance.Value;

        private readonly LoggerConfig _config;
        private readonly Dictionary<string, LogTemplate> _templates = new();
        private readonly ReaderWriterLockSlim _templateLock = new(LockRecursionPolicy.SupportsRecursion);
        private readonly Channel<(LogMessage Message, LogMessage[] Array)> _logChannel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _logWriterTask;
        private FileStream _fileStream;
        private StreamWriter _writer;
        private int _bufferCount;
        private bool _isDisposed;
        private bool _isDisposing;
        private readonly Counter _queueFullCounter = new();
        private readonly Counter _totalLogsProcessed = new();
        private readonly System.Diagnostics.Stopwatch _throughputWatch = System.Diagnostics.Stopwatch.StartNew();

        [ThreadStatic] private static ConsoleColor _originalColor;
        [ThreadStatic] private static bool _colorInitialized;

        private static readonly ArrayPool<LogMessage> _messagePool =
            ArrayPool<LogMessage>.Create(maxArrayLength: 1024, maxArraysPerBucket: 50);

        private readonly Encoding _utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private readonly Dictionary<string, byte[]> _templateCache = new();
        private readonly Memory<byte> _buffer = new byte[8192];

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

        private LoggerInstance() : this(new LoggerConfig()) { }

        public LoggerInstance(LoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            _logChannel = Channel.CreateBounded<(LogMessage, LogMessage[])>(new BoundedChannelOptions(_config.MaxQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true
            });

            AddTemplate(new LogTemplate
            {
                Name = "Default",
                Template = "{Timestamp} [{Level}] [{ThreadId}] {Message}{Exception}{Properties}",
                Level = LogLevel.Information,
                IncludeException = true
            });

            InitializeFileWriter();

            _logWriterTask = Task.Factory.StartNew(
                ProcessLogQueueAsync,
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            Task.Factory.StartNew(MonitorPerformance, _cts.Token,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

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

        public void AddTemplate(LogTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            _templateLock.EnterWriteLock();
            try
            {
                _templates[template.Name] = template;
                _templateCache.Remove(template.Name);
            }
            finally
            {
                _templateLock.ExitWriteLock();
            }
        }

        public void RemoveTemplate(string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return;

            _templateLock.EnterWriteLock();
            try
            {
                if (_templates.ContainsKey(templateName))
                {
                    _templates.Remove(templateName);
                }
                _templateCache.Remove(templateName);
            }
            finally
            {
                _templateLock.ExitWriteLock();
            }
        }

        public LogTemplate GetTemplate(string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) templateName = "Default";

            _templateLock.EnterReadLock();
            try
            {
                return _templates.TryGetValue(templateName, out var template)
                    ? template
                    : _templates["Default"];
            }
            finally
            {
                _templateLock.ExitReadLock();
            }
        }

        private (LogMessage Message, LogMessage[] Array) GetLogMessage()
        {
            var array = _messagePool.Rent(1);
            var message = array[0];

            if (message == null)
            {
                message = new LogMessage();
                array[0] = message;
            }

            return (message, array);
        }

        private void ReturnLogMessage(LogMessage[] array)
        {
            _messagePool.Return(array);
        }

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

            if (level < template.Level) return;

            var (logMessage, array) = GetLogMessage();

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

            EnqueueLogMessage(logMessage, array);
        }

        private void EnqueueLogMessage(LogMessage message, LogMessage[] array)
        {
            if (_isDisposing)
            {
                ReturnLogMessage(array);
                return;
            }

            if (!_logChannel.Writer.TryWrite((message, array)))
            {
                _queueFullCounter.Increment();

                if (_queueFullCounter.Value % 1000 == 0)
                {
                    var (warningMsg, warningArray) = GetLogMessage();
                    warningMsg.Timestamp = DateTime.Now;
                    warningMsg.Level = LogLevel.Warning;
                    warningMsg.Message = $"日志队列已满，已丢弃 {_queueFullCounter.Value} 条日志";
                    warningMsg.ThreadId = Environment.CurrentManagedThreadId;

                    WriteToConsoleDirect(warningMsg);
                    ReturnLogMessage(warningArray);
                }

                ReturnLogMessage(array);
            }
        }

        private void WriteToConsoleDirect(LogMessage message)
        {
            if (!_config.EnableConsoleColor)
            {
                Console.WriteLine(FormatMessage(message));
                return;
            }

            var color = GetConsoleColor(message.Level);

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

        private void WriteToConsole(LogMessage message)
        {
            if (!_config.EnableConsoleColor)
            {
                Console.WriteLine(FormatMessage(message));
                return;
            }

            var color = GetConsoleColor(message.Level);

            if (!_colorInitialized)
            {
                _originalColor = Console.ForegroundColor;
                _colorInitialized = true;
            }

            try
            {
                Console.ForegroundColor = color;
                Console.Write(FormatMessage(message));
                Console.Write(Environment.NewLine);
            }
            finally
            {
                Console.ForegroundColor = _originalColor;
            }
        }

        private async Task ProcessLogQueueAsync()
        {
            var batchSize = _config.BatchSize;
            var messageList = new List<(LogMessage Message, LogMessage[] Array)>(batchSize);

            try
            {
                await foreach (var (message, array) in _logChannel.Reader.ReadAllAsync(_cts.Token))
                {
                    messageList.Add((message, array));

                    while (messageList.Count < batchSize && _logChannel.Reader.TryRead(out var item))
                    {
                        messageList.Add(item);
                    }

                    try
                    {
                        await ProcessBatch(messageList.Select(m => m.Message).ToList());
                    }
                    finally
                    {
                        foreach (var item in messageList)
                        {
                            ReturnLogMessage(item.Array);
                        }
                        messageList.Clear();
                    }

                    _totalLogsProcessed.Add(messageList.Count);
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
                while (_logChannel.Reader.TryRead(out var result))
                {
                    var (message, array) = result;
                    try
                    {
                        // 使用同步方法而非异步方法
                        if (message.Level >= FileLogLevel)
                        {
                            _writer.WriteLine(FormatMessage(message));
                        }

                        if (message.Level >= ConsoleLogLevel)
                        {
                            WriteToConsole(message);
                        }
                    }
                    finally
                    {
                        ReturnLogMessage(array);
                    }
                }

                await FlushWriterAsync();
            }
        }

        private async Task ProcessBatch(List<LogMessage> messages)
        {
            var fileTasks = new List<Task>();

            foreach (var message in messages)
            {
                if (message.Level >= FileLogLevel)
                {
                    fileTasks.Add(SafeWriteToFileAsync(message));
                }

                if (message.Level >= ConsoleLogLevel)
                {
                    WriteToConsole(message);
                }
            }

            if (fileTasks.Count > 0)
            {
                await Task.WhenAll(fileTasks);
            }
        }

        private async Task SafeWriteToFileAsync(LogMessage message)
        {
            int retry = _config.MaxRetryCount;
            while (retry-- > 0)
            {
                try
                {
                    await WriteToFileAsync(message);
                    return;
                }
                catch (Exception ex)
                {
                    if (retry == 0)
                    {
                        var (errorMessage, array) = GetLogMessage();
                        errorMessage.Timestamp = DateTime.Now;
                        errorMessage.Level = LogLevel.Critical;
                        errorMessage.Message = $"日志写入失败，已达到最大重试次数: {ex.Message}";
                        errorMessage.ThreadId = Environment.CurrentManagedThreadId;
                        errorMessage.Properties = new Dictionary<string, object>
                        {
                            ["OriginalMessage"] = message.Message,
                            ["OriginalLevel"] = message.Level.ToString()
                        };

                        WriteToConsoleDirect(errorMessage);
                        ReturnLogMessage(array);
                    }
                    else
                    {
                        await Task.Delay(_config.RetryDelayMs);
                    }
                }
            }
        }

        private async Task WriteToFileAsync(LogMessage message)
        {
            if (_isDisposing) return;

            try
            {
                var formatted = FormatMessage(message);
                var bytes = _utf8.GetBytes(formatted);
                await _fileStream.WriteAsync(bytes, 0, bytes.Length, _cts.Token);
                _bufferCount++;

                if (_bufferCount >= _config.FlushInterval)
                {
                    await _fileStream.FlushAsync(_cts.Token);
                    _bufferCount = 0;
                }
            }
            catch (Exception ex)
            {
                var (errorMessage, array) = GetLogMessage();
                errorMessage.Timestamp = DateTime.Now;
                errorMessage.Level = LogLevel.Error;
                errorMessage.Message = $"日志写入失败: {ex.Message}";
                errorMessage.ThreadId = Environment.CurrentManagedThreadId;

                WriteToConsole(errorMessage);
                ReturnLogMessage(array);

                ResetFileWriter();

                throw;
            }
        }

        private async Task FlushWriterAsync()
        {
            try
            {
                if (_writer != null)
                {
                    await _writer.FlushAsync();
                }

                if (_fileStream != null)
                {
                    await _fileStream.FlushAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"刷新日志缓冲区失败: {ex.Message}");
            }
        }

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

        private async Task MonitorPerformance()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(5000, _cts.Token);

                    var elapsed = _throughputWatch.Elapsed.TotalSeconds;
                    var logsPerSecond = _totalLogsProcessed.Value / elapsed;
                    var queueLength = _logChannel.Reader.Count;

                    Console.WriteLine($"[性能监控] 吞吐量: {logsPerSecond:N0} 条/秒 | 队列长度: {queueLength} | 总处理量: {_totalLogsProcessed.Value:N0} | 丢弃日志: {_queueFullCounter.Value:N0}");
                }
            }
            catch (OperationCanceledException)
            {
                // 正常终止
            }
            catch (Exception ex)
            {
                Console.WriteLine($"性能监控任务异常: {ex}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed || _isDisposing) return;
            _isDisposing = true;

            try
            {
                _cts.Cancel();

                Task.WaitAll(
                    new[] { _logWriterTask },
                    TimeSpan.FromSeconds(10));

                // 修复：正确的元组解构语法
                while (_logChannel.Reader.TryRead(out var result))
                {
                    var (message, array) = result;
                    try
                    {
                        if (message.Level >= FileLogLevel)
                        {
                            _writer.WriteLine(FormatMessage(message));
                        }
                    }
                    finally
                    {
                        ReturnLogMessage(array);
                    }
                }

                _writer?.Flush();
                _fileStream?.Flush();
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
                _logChannel.Writer.TryComplete();
                _writer?.Dispose();
                _fileStream?.Dispose();
                _cts.Dispose();
                _isDisposed = true;
                _isDisposing = false;
            }
        }

        private string FormatMessage(LogMessage message, string templateName = "Default")
        {
            if (message == null)
                return string.Empty;

            var template = GetTemplate(templateName);
            var formatted = template.Template
                .Replace("{Timestamp}", message.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Replace("{Level}", message.LevelMessage)
                .Replace("{Message}", message.Message)
                .Replace("{ThreadId}", message.ThreadId.ToString());

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

        private string FormatStructuredMessage<T>(T state, Exception exception, Func<T, Exception, string> formatter)
        {
            if (formatter != null)
                return formatter(state, exception);

            if (state is string str)
                return str;

            return state?.ToString() ?? "";
        }

        private class Counter
        {
            private long _value;
            public long Value => _value;

            public void Increment() => Interlocked.Increment(ref _value);
            public void Add(long amount) => Interlocked.Add(ref _value, amount);
        }
    }
}
