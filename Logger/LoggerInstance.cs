using Microsoft.Win32.SafeHandles;
using Server.Common.Extensions;
using Server.Logger;
using Server.Logger.Common;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Server.Logger
{
    // 日志配置类
    public sealed class LoggerConfig
    {
        public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Trace;
        public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
        public string LogFilePath { get; set; } = "application.log";
        public bool EnableAsyncWriting { get; set; } = true;
        public bool EnableConsoleWriting { get; set; } = true;
        public int MaxQueueSize { get; set; } = 1_000_000;
        public int BatchSize { get; set; } = 10_000;
        public int FlushInterval { get; set; } = 500;
        public bool EnableConsoleColor { get; set; } = true;
        public int MaxRetryCount { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 100;
        public int FileBufferSize { get; set; } = 64 * 1024; // 64KB
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
        public bool UseMemoryMappedFile { get; set; } = true;
        public long MemoryMappedFileSize { get; set; } = 1024 * 1024 * 100; // 100MB
    }

    // 日志消息类 - 使用struct减少GC压力
    public readonly struct LogMessage
    {
        public readonly DateTime Timestamp;
        public readonly LogLevel Level;
        public readonly ReadOnlyMemory<byte> Message;
        public readonly int ThreadId;
        public readonly string ThreadName;
        public readonly Exception Exception;
        public readonly IReadOnlyDictionary<string, object> Properties;

        private readonly byte[] _levelBytes;

        public LogMessage(DateTime timestamp, LogLevel level, ReadOnlyMemory<byte> message, int threadId, string threadName,
            Exception exception = null, IReadOnlyDictionary<string, object> properties = null)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
            ThreadId = threadId;
            ThreadName = threadName;
            Exception = exception;
            Properties = properties;

            _levelBytes = Encoding.UTF8.GetBytes(level.ToString().Center(11, " ").ToUpperInvariant());
        }
        ReadOnlySpan<byte> LevelMessage => _levelBytes.AsSpan();
    }

    // 日志模板配置
    public sealed class LogTemplate
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
        void AddTemplate(LogTemplate template);
        void RemoveTemplate(string templateName);
        LogTemplate GetTemplate(string templateName);

        void Log(LogLevel level, ReadOnlyMemory<byte> message, Exception exception = null,
            IReadOnlyDictionary<string, object> properties = null, string templateName = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0);

        // 泛型日志方法
        void Log<T>(LogLevel level, T state, Func<T, Exception, string> formatter = null,
            Exception exception = null, IReadOnlyDictionary<string, object> properties = null,
            string templateName = null);

        // 快捷日志方法
        void LogTrace(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogDebug(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogInformation(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogWarning(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogError(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogCritical(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null);

        // 泛型快捷方法
        void LogTrace<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogDebug<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogInformation<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogWarning<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogError<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogCritical<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
    }

    // Windows高性能日志实现
    public sealed class LoggerInstance : ILogger, IDisposable
    {
        // 单例实现
        private static readonly Lazy<LoggerInstance> _instance = new Lazy<LoggerInstance>(
            () => new LoggerInstance(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static LoggerInstance Instance => _instance.Value;

        // 配置和状态
        private readonly LoggerConfig _config;
        private readonly ConcurrentDictionary<string, LogTemplate> _templates = new ConcurrentDictionary<string, LogTemplate>();
        private readonly Channel<LogMessage> _logChannel;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task[] _processingTasks;
        private readonly List<Task> _allTasks = new List<Task>();
        private bool _isDisposed;
        private bool _isAsyncWrite;
        private bool _isConsoleWrite;

        // 文件写入相关
        private FileStream _fileStream;
        private readonly byte[] _writeBuffer;
        private int _bufferOffset;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly object _flushLock = new object();

        // 内存映射文件相关 (Windows优化)
        private IntPtr _fileHandle = IntPtr.Zero;
        private IntPtr _mapHandle = IntPtr.Zero;
        private IntPtr _mapView = IntPtr.Zero;
        private long _mapOffset;
        private readonly bool _useMemoryMappedFile;
        private readonly long _memoryMappedFileSize;

        // 性能监控
        private readonly Counter _totalLogsProcessed = new Counter();
        private readonly Counter _queueFullCounter = new Counter();
        private readonly System.Diagnostics.Stopwatch _performanceWatch = System.Diagnostics.Stopwatch.StartNew();

        // 控制台输出相关
        private readonly BlockingCollection<LogMessage> _consoleQueue = new BlockingCollection<LogMessage>();
        private readonly Thread _consoleThread;

        // 初始化
        private LoggerInstance() : this(new LoggerConfig()) { }

        public LoggerInstance(LoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _writeBuffer = new byte[_config.FileBufferSize];
            _useMemoryMappedFile = _config.UseMemoryMappedFile;
            _memoryMappedFileSize = _config.MemoryMappedFileSize;
            _isAsyncWrite = _config.EnableAsyncWriting;
            _isConsoleWrite = _config.EnableConsoleWriting;

            // 初始化日志通道
            _logChannel = Channel.CreateBounded<LogMessage>(new BoundedChannelOptions(_config.MaxQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = false
            });

            // 添加默认模板
            AddTemplate(new LogTemplate
            {
                Name = "Default",
                Template = "{Timestamp} [{Level}] [{ThreadId}] {Message}{Exception}{Properties}",
                Level = LogLevel.Information,
                IncludeException = true
            });

            // 初始化文件写入器
            InitializeFileWriter();

            if (_isAsyncWrite)
            {
                // 启动处理任务
                _processingTasks = new Task[_config.MaxDegreeOfParallelism];
                for (int i = 0; i < _config.MaxDegreeOfParallelism; i++)
                {
                    _processingTasks[i] = Task.Factory.StartNew(
                        ProcessLogQueueAsync,
                        _cts.Token,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default);

                    _allTasks.Add(_processingTasks[i]);
                }

                // 启动性能监控任务
                _allTasks.Add(Task.Factory.StartNew(MonitorPerformance, _cts.Token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }
            if (_isConsoleWrite) 
            {
                // 启动控制台输出线程
                _consoleThread = new Thread(ProcessConsoleQueue) { IsBackground = true };
                _consoleThread.Start();
            }
        }
        private SafeFileHandle _safeFileHandle;
        private SafeMapHandle _safeMapHandle;
        // 初始化文件写入器
        private void InitializeFileWriter()
        {
            try
            {
                // 检查权限和锁定状态
                if (!CheckFileWritePermission(_config.LogFilePath))
                {
                    throw new InvalidOperationException("没有写入日志文件的权限");
                }

                if (IsFileLocked(_config.LogFilePath))
                {
                    Console.WriteLine($"日志文件已被锁定: {_config.LogFilePath}");
                    // 可以选择等待或抛出异常
                }

                if (_useMemoryMappedFile)
                {
                    // 使用内存映射文件 (Windows优化)
                    IntPtr fileHandle = CreateFile(
                        _config.LogFilePath,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_ALWAYS,
                        FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero);

                    if (fileHandle == INVALID_HANDLE_VALUE)
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        Console.WriteLine($"CreateFile failed with error code: {errorCode}");
                        throw new Win32Exception(errorCode);
                    }

                    // 使用SafeFileHandle管理文件句柄
                    _safeFileHandle = new SafeFileHandle(fileHandle, true);

                    // 设置文件初始大小
                    if (_config.MemoryMappedFileSize > 0)
                    {
                        long fileSize = _config.MemoryMappedFileSize;
                        long newPointer;

                        if (!SetFilePointerEx(_safeFileHandle.DangerousGetHandle(), fileSize - 1, out newPointer, 0))
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            Console.WriteLine($"SetFilePointerEx failed with error code: {errorCode}");
                            throw new Win32Exception(errorCode);
                        }

                        if (!SetEndOfFile(_safeFileHandle.DangerousGetHandle()))
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            Console.WriteLine($"SetEndOfFile failed with error code: {errorCode}");
                            throw new Win32Exception(errorCode);
                        }

                        // 重置文件指针
                        if (!SetFilePointerEx(_safeFileHandle.DangerousGetHandle(), 0, out newPointer, 0))
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            Console.WriteLine($"SetFilePointerEx failed with error code: {errorCode}");
                            throw new Win32Exception(errorCode);
                        }
                    }

                    // 创建内存映射 - 使用SafeHandle的DangerousGetHandle()方法
                    IntPtr mapHandle = CreateFileMapping(
                        _safeFileHandle.DangerousGetHandle(),  // 修正：使用SafeHandle的句柄
                        IntPtr.Zero,
                        PAGE_READWRITE,
                        (uint)(_memoryMappedFileSize >> 32),
                        (uint)(_memoryMappedFileSize & 0xFFFFFFFF),
                        null);

                    if (mapHandle == IntPtr.Zero)
                    {
                        int errorCode = Marshal.GetLastWin32Error();

                        // 记录详细的诊断信息
                        Console.WriteLine($"CreateFileMapping failed with error code: {errorCode}");
                        Console.WriteLine($"Error message: {new Win32Exception(errorCode).Message}");
                        Console.WriteLine($"File path: {_config.LogFilePath}");
                        Console.WriteLine($"MemoryMappedFileSize: {_memoryMappedFileSize} bytes");
                        Console.WriteLine($"SafeFileHandle IsInvalid: {_safeFileHandle.IsInvalid}");

                        // 尝试获取文件状态信息
                        try
                        {
                            using (FileStream testStream = new FileStream(
                                _config.LogFilePath,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.ReadWrite))
                            {
                                Console.WriteLine($"File size: {testStream.Length} bytes");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error accessing file: {ex.Message}");
                        }

                        // 释放资源 - 只使用SafeHandle.Dispose()
                        _safeFileHandle.Dispose();
                        _safeFileHandle = null;

                        throw new Win32Exception(errorCode, $"CreateFileMapping failed with error code {errorCode}");
                    }

                    // 使用SafeMapHandle管理映射句柄
                    _safeMapHandle = new SafeMapHandle();
                    _safeMapHandle.Initialize(mapHandle);

                    // 映射视图 - 使用SafeMapHandle的句柄
                    _mapView = MapViewOfFile(
                        _safeMapHandle.DangerousGetHandle(),  // 修正：使用SafeHandle的句柄
                        FILE_MAP_WRITE,
                        0,
                        0,
                        (UIntPtr)_memoryMappedFileSize);

                    if (_mapView == IntPtr.Zero)
                    {
                        int errorCode = Marshal.GetLastWin32Error();
                        Console.WriteLine($"MapViewOfFile failed with error code: {errorCode}");

                        // 释放资源
                        _safeMapHandle.Dispose();
                        _safeMapHandle = null;
                        _safeFileHandle.Dispose();
                        _safeFileHandle = null;

                        throw new Win32Exception(errorCode);
                    }

                    _mapOffset = 0;
                }
                else
                {
                    // 使用传统文件流
                    _fileStream?.Dispose();
                    _fileStream = new FileStream(
                        _config.LogFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite,
                        _config.FileBufferSize,
                        useAsync: true);

                    _bufferOffset = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize file writer: {ex}");
                // 确保资源释放
                Dispose();
                throw;
            }
        }
        private bool CheckFileWritePermission(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.OpenOrCreate,
                                                      FileAccess.ReadWrite, FileShare.None))
                {
                    // 文件可读写
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"没有写入权限: {filePath}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查文件权限时出错: {ex.Message}");
                return false;
            }
        }
        private bool IsFileLocked(string filePath)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open,
                                                      FileAccess.ReadWrite, FileShare.None))
                {
                    // 文件未被锁定
                }
            }
            catch (IOException)
            {
                return true; // 文件被锁定
            }
            return false;
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

        // 核心日志方法
        public void Log(LogLevel level, ReadOnlyMemory<byte> message, Exception exception = null,
            IReadOnlyDictionary<string, object> properties = null, string templateName = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0)
        {
            if (_isDisposed) return;

            var template = GetTemplate(templateName);
            if (level < template.Level) return;

            var logMessage = new LogMessage(
                DateTime.Now,
                level,
                message,
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.Name,
                exception,
                properties ?? new Dictionary<string, object>
                {
                    ["CallerMember"] = memberName,
                    ["CallerFile"] = Path.GetFileName(sourceFilePath),
                    ["CallerLine"] = sourceLineNumber
                });

            if (_isAsyncWrite)
            {
                EnqueueLogMessage(logMessage);
            }
            else
            {
                if (_isConsoleWrite && logMessage.Level > _config.ConsoleLogLevel)
                {
                    WriteToConsoleDirect(logMessage);
                }
                if (logMessage.Level > _config.FileLogLevel)
                {
                    WriteToFileStream(logMessage);
                }
            }
        }

        // 泛型日志方法
        public void Log<T>(LogLevel level, T state, Func<T, Exception, string> formatter = null,
            Exception exception = null, IReadOnlyDictionary<string, object> properties = null,
            string templateName = null)
        {
            if (_isDisposed) return;

            var template = GetTemplate(templateName);
            if (level < template.Level) return;

            string messageStr;
            if (formatter != null)
            {
                messageStr = formatter(state, exception);
            }
            else if (state is string str)
            {
                messageStr = str;
            }
            else
            {
                messageStr = state?.ToString() ?? "";
            }

            var messageBytes = Encoding.UTF8.GetBytes(messageStr);
            var logMessage = new LogMessage(
                DateTime.Now,
                level,
                messageBytes,
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.Name,
                exception,
                properties);

            if (_isAsyncWrite)
            {
                EnqueueLogMessage(logMessage);
            }
            else
            {
                if (_isConsoleWrite && logMessage.Level > _config.ConsoleLogLevel)
                {
                    WriteToConsoleDirect(logMessage);
                }
                if (logMessage.Level > _config.FileLogLevel)
                {
                    WriteToFileStream(logMessage);
                }
            }
        }

        // 入队日志消息
        private void EnqueueLogMessage(LogMessage message)
        {
            if (_isDisposed) return;

            if (!_logChannel.Writer.TryWrite(message))
            {
                _queueFullCounter.Increment();

                // 队列满时记录警告
                if (_queueFullCounter.Value % 1000 == 0)
                {
                    var warningMsg = new LogMessage(
                        DateTime.Now,
                        LogLevel.Warning,
                        Encoding.UTF8.GetBytes($"日志队列已满，已丢弃 {_queueFullCounter.Value} 条日志"),
                        Environment.CurrentManagedThreadId,
                        Thread.CurrentThread.Name);

                    WriteToConsoleDirect(warningMsg);
                }
            }
        }

        // 处理日志队列
        private async Task ProcessLogQueueAsync()
        {
            try
            {
                await foreach (var message in _logChannel.Reader.ReadAllAsync(_cts.Token))
                {
                    try
                    {
                        if (message.Level >= _config.FileLogLevel)
                        {
                            WriteToFile(message);
                        }

                        if (_isConsoleWrite && message.Level >= _config.ConsoleLogLevel)
                        {
                            EnqueueConsoleMessage(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"处理日志消息时发生异常: {ex}");
                    }
                    finally
                    {
                        _totalLogsProcessed.Increment();
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
        }

        // 写入文件
        private void WriteToFile(LogMessage message)
        {
            if (_isDisposed) return;

            try
            {
                if (_useMemoryMappedFile)
                {
                    // 使用内存映射文件写入
                    WriteToMemoryMappedFile(message);
                }
                else
                {
                    // 使用传统文件流写入
                    WriteToFileStream(message);
                }
            }
            catch (Exception ex)
            {
                var errorMessage = new LogMessage(
                    DateTime.Now,
                    LogLevel.Error,
                    Encoding.UTF8.GetBytes($"日志写入失败: {ex.Message}"),
                    Environment.CurrentManagedThreadId,
                    Thread.CurrentThread.Name);

                WriteToConsoleDirect(errorMessage);

                // 重置文件写入器
                ResetFileWriter();

                throw;
            }
        }

        // 使用内存映射文件写入 (Windows优化)
        private unsafe void WriteToMemoryMappedFile(LogMessage message)
        {
            // 格式化消息
            var formattedMessage = FormatMessage(message);

            // 确保有足够空间
            if (_mapOffset + formattedMessage.Length > _memoryMappedFileSize)
            {
                FlushMemoryMappedFile();
                _mapOffset = 0;
            }

            // 写入到内存映射区域
            byte* mapPtr = (byte*)_mapView + _mapOffset;
            formattedMessage.Span.CopyTo(new Span<byte>(mapPtr, formattedMessage.Length));
            _mapOffset += formattedMessage.Length;

            // 定期刷新到磁盘
            if (_mapOffset % (1024 * 1024) < formattedMessage.Length) // 每1MB刷新一次
            {
                FlushViewOfFile((IntPtr)mapPtr, (UIntPtr)formattedMessage.Length);
            }
        }

        // 刷新内存映射文件到磁盘
        private void FlushMemoryMappedFile()
        {
            if (_mapView != IntPtr.Zero)
            {
                FlushViewOfFile(_mapView, (UIntPtr)_mapOffset);
            }
        }

        // 使用文件流写入
        private void WriteToFileStream(LogMessage message)
        {
            var formattedMessage = FormatMessage(message);
            _writeLock.Wait();
            try
            {
                while (formattedMessage.Length > 0)
                {
                    int spaceLeft = _writeBuffer.Length - _bufferOffset;
                    if (spaceLeft == 0)
                    {
                        _fileStream.Write(_writeBuffer, 0, _writeBuffer.Length); // 同步写入
                        _bufferOffset = 0;
                        spaceLeft = _writeBuffer.Length;
                    }

                    int copyLength = Math.Min(spaceLeft, formattedMessage.Length);
                    formattedMessage.Span.Slice(0, copyLength).CopyTo(_writeBuffer.AsSpan(_bufferOffset));
                    _bufferOffset += copyLength;
                    formattedMessage = formattedMessage.Slice(copyLength);
                }

                if (_bufferOffset >= _config.FlushInterval)
                {
                    _fileStream.Write(_writeBuffer, 0, _bufferOffset); // 同步写入
                    _bufferOffset = 0;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // 重置文件写入器
        private void ResetFileWriter()
        {
            try
            {
                if (_useMemoryMappedFile)
                {
                    if (_mapView != IntPtr.Zero)
                    {
                        UnmapViewOfFile(_mapView);
                        _mapView = IntPtr.Zero;
                    }

                    if (_mapHandle != IntPtr.Zero)
                    {
                        CloseHandle(_mapHandle);
                        _mapHandle = IntPtr.Zero;
                    }

                    if (_fileHandle != IntPtr.Zero)
                    {
                        CloseHandle(_fileHandle);
                        _fileHandle = IntPtr.Zero;
                    }
                }
                else
                {
                    _fileStream?.Flush();
                    _fileStream?.Dispose();
                }

                InitializeFileWriter();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重置日志文件失败: {ex.Message}");
            }
        }

        // 格式化消息
        private Memory<byte> FormatMessage(LogMessage message)
        {
            using var ms = new MemoryStream();
            var template = GetTemplate(null);

            // 写入时间戳
            var timestamp = message.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            ms.Write(Encoding.UTF8.GetBytes(timestamp));
            ms.WriteByte((byte)' ');

            // 写入日志级别
            ms.WriteByte((byte)'[');
            ms.Write(Encoding.UTF8.GetBytes(message.Level.ToString().ToUpperInvariant()));
            ms.WriteByte((byte)']');
            ms.WriteByte((byte)' ');

            // 写入线程ID
            ms.WriteByte((byte)'[');
            ms.Write(Encoding.UTF8.GetBytes(message.ThreadId.ToString()));
            ms.WriteByte((byte)']');
            ms.WriteByte((byte)' ');

            // 写入消息内容
            ms.Write(message.Message.Span);

            // 写入异常信息（如果有）
            if (template.IncludeException && message.Exception != null)
            {
                ms.WriteByte((byte)'\n');
                ms.Write(Encoding.UTF8.GetBytes($"[Exception] {message.Exception.GetType().FullName}: {message.Exception.Message}"));
                if (!string.IsNullOrEmpty(message.Exception.StackTrace))
                {
                    ms.WriteByte((byte)'\n');
                    ms.Write(Encoding.UTF8.GetBytes(message.Exception.StackTrace));
                }
            }

            // 写入属性信息（如果有）
            if (message.Properties != null && message.Properties.Count > 0)
            {
                ms.WriteByte((byte)'\n');
                ms.Write(Encoding.UTF8.GetBytes("[Properties] "));
                bool first = true;
                foreach (var property in message.Properties)
                {
                    if (!first) ms.WriteByte((byte)',');
                    ms.Write(Encoding.UTF8.GetBytes($"{property.Key}={property.Value?.ToString() ?? "null"}"));
                    first = false;
                }
            }

            // 写入换行符
            ms.WriteByte((byte)'\r');
            ms.WriteByte((byte)'\n');

            // 返回内存流的内容
            return ms.ToArray();
        }

        // 控制台输出相关
        private void EnqueueConsoleMessage(LogMessage message)
        {
            try
            {
                _consoleQueue.Add(message);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"格式化控制台日志失败: {ex.Message}");
            }
        }

        private void ProcessConsoleQueue()
        {
            try
            {
                foreach (var message in _consoleQueue.GetConsumingEnumerable())
                {
                    WriteToConsoleDirect(message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"控制台输出线程异常: {ex.Message}");
            }
        }

        private void WriteToConsoleDirect(LogMessage message)
        {
            try
            {
                var formatted = FormatMessage(message);
                var formattedStr = Encoding.UTF8.GetString(formatted.Span);

                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = GetConsoleColor(message.Level);
                Console.Write(formattedStr);
                Console.ForegroundColor = originalColor;  
            }
            catch (Exception ex)
            {
                Console.WriteLine($"直接写入控制台失败: {ex.Message}");
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

        // 性能监控
        private async Task MonitorPerformance()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(5000, _cts.Token);

                    var elapsed = _performanceWatch.Elapsed.TotalSeconds;
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

        // 实现IDisposable
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _cts.Cancel();

                // 等待处理任务完成
                Task.WaitAll(_allTasks.ToArray(), TimeSpan.FromSeconds(10));

                // 清空队列
                while (_logChannel.Reader.TryRead(out var message))
                {
                    try
                    {
                        if (message.Level >= _config.FileLogLevel)
                        {
                            if (_useMemoryMappedFile)
                            {
                                WriteToMemoryMappedFile(message);
                            }
                            else
                            {
                                WriteToFileStream(message);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"关闭时处理日志失败: {ex.Message}");
                    }
                }

                // 刷新缓冲区
                if (_useMemoryMappedFile)
                {
                    FlushMemoryMappedFile();
                }
                else
                {
                    _fileStream?.Flush();
                }
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
                // 清理资源
                if (_useMemoryMappedFile)
                {
                    if (_mapView != IntPtr.Zero)
                    {
                        UnmapViewOfFile(_mapView);
                        _mapView = IntPtr.Zero;
                    }

                    if (_mapHandle != IntPtr.Zero)
                    {
                        CloseHandle(_mapHandle);
                        _mapHandle = IntPtr.Zero;
                    }

                    if (_fileHandle != IntPtr.Zero)
                    {
                        CloseHandle(_fileHandle);
                        _fileHandle = IntPtr.Zero;
                    }
                }
                else
                {
                    _fileStream?.Dispose();
                }

                _consoleQueue.CompleteAdding();
                _cts.Dispose();
            }
        }

        // 快捷日志方法
        public void LogTrace(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Trace, message, null, properties, templateName);

        public void LogDebug(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Debug, message, null, properties, templateName);

        public void LogInformation(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Information, message, null, properties, templateName);

        public void LogWarning(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Warning, message, exception, properties, templateName);

        public void LogError(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Error, message, exception, properties, templateName);

        public void LogCritical(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null)
            => Log(LogLevel.Critical, message, exception, properties, templateName);

        // 泛型快捷方法
        public void LogTrace<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Trace, state, formatter, null, null, templateName);

        public void LogDebug<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Debug, state, formatter, null, null, templateName);

        public void LogInformation<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Information, state, formatter, null, null, templateName);

        public void LogWarning<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Warning, state, formatter, exception, null, templateName);

        public void LogError<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Error, state, formatter, exception, null, templateName);

        public void LogCritical<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null)
            => Log(LogLevel.Critical, state, formatter, exception, null, templateName);

        private sealed class SafeMapHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr handle);

            public SafeMapHandle() : base(true) { }

            // 添加公共方法来设置句柄
            public void Initialize(IntPtr handle)
            {
                base.SetHandle(handle);
            }
            protected override bool ReleaseHandle()
            {
                return CloseHandle(handle);
            }
        }
        // Windows API P/Invoke
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFileMapping(
            IntPtr hFile,
            IntPtr lpFileMappingAttributes,
            uint flProtect,
            uint dwMaximumSizeHigh,
            uint dwMaximumSizeLow,
            string lpName);
        // P/Invoke 声明

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(
            IntPtr hFileMappingObject,
            uint dwDesiredAccess,
            uint dwFileOffsetHigh,
            uint dwFileOffsetLow,
            UIntPtr dwNumberOfBytesToMap);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(
            IntPtr hFile,
            long liDistanceToMove,
            out long lpNewFilePointer,
            uint dwMoveMethod);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetEndOfFile(IntPtr hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] 
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] 
        private static extern bool FlushViewOfFile(IntPtr lpBaseAddress, UIntPtr dwNumberOfBytesToFlush);

        // Windows API常量
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_ALWAYS = 4;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint PAGE_READWRITE = 0x04;
        private const uint FILE_MAP_WRITE = 0x0002;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        // 原子计数器
        private sealed class Counter
        {
            private long _value;
            public long Value => _value;

            public void Increment() => Interlocked.Increment(ref _value);
            public void Add(long amount) => Interlocked.Add(ref _value, amount);
        }
    }
}