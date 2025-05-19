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
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Server.Logger
{
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
        private volatile bool _isAsyncWrite; // 确保多线程可见性

        // 文件写入相关
        private FileStream _fileStream;
        private readonly byte[] _writeBuffer;
        private int _bufferOffset;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly object _mmfSwitchLock = new object(); // 内存映射切换锁

        // 内存映射文件相关 (Windows优化)
        private IntPtr _mapView = IntPtr.Zero;
        private long _mapOffset;
        private volatile bool _useMemoryMappedFile; // 确保多线程可见性
        private readonly long _memoryMappedFileSize;
        private SafeFileHandle _safeFileHandle;
        private SafeMapHandle _safeMapHandle;
        private Task _createNewFileTask = Task.CompletedTask; // 记录当前创建任务
        private int _currentFileIndex = 0;
        private DateTime _lastFileDate = DateTime.MinValue;

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
            Console.OutputEncoding = Encoding.UTF8;

            _config = config ?? throw new ArgumentNullException(nameof(config));
            _writeBuffer = new byte[_config.FileBufferSize];
            _useMemoryMappedFile = _config.UseMemoryMappedFile;
            _memoryMappedFileSize = _config.MemoryMappedFileSize;
            _isAsyncWrite = _config.EnableAsyncWriting;

            // 初始化日志通道
            _logChannel = Channel.CreateBounded<LogMessage>(new BoundedChannelOptions(_config.MaxQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = false
            });
            Console.WriteLine($"current maxqueue num {_config.MaxQueueSize}");
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

                // 启动控制台输出线程
                _consoleThread = new Thread(ProcessConsoleQueue) { IsBackground = true };
                _consoleThread.Start();

                // 启动性能监控任务
                _allTasks.Add(Task.Factory.StartNew(MonitorPerformance, _cts.Token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }
        }

        // 初始化文件写入器（核心逻辑）
        private void InitializeFileWriter()
        {
            try
            {
                // 确保日志目录存在
                EnsureLogDirectoryExists(_config.LogDirectory);

                // 获取当前日期（按天重置序号）
                DateTime now = DateTime.Now;
                string currentDate = now.ToString("yyyyMMdd");
                _lastFileDate = now.Date; // 记录当前日期

                // 按日期重置序号（如果日期变更）
                if (currentDate != _lastFileDate.ToString("yyyyMMdd"))
                {
                    _currentFileIndex = 0;
                    _lastFileDate = now.Date;
                }

                string baseFileName;
                do
                {
                    baseFileName = Path.Combine(_config.LogDirectory,
                        string.Format(_config.LogFileNameFormat, now, _currentFileIndex));
                    _currentFileIndex++; // 先尝试当前序号，失败后递增
                } while (File.Exists(baseFileName));

                // 回退到上一个有效序号（因为循环中多递增了一次）
                _currentFileIndex--;
                baseFileName = Path.Combine(_config.LogDirectory,
                    string.Format(_config.LogFileNameFormat, now, _currentFileIndex));

                // 初始化文件写入（内存映射或文件流）
                if (_useMemoryMappedFile)
                {
                    InitializeMemoryMappedFile(baseFileName, _memoryMappedFileSize);
                }
                else
                {
                    InitializeFileStream(baseFileName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化文件写入器失败: {ex}");
                Dispose();
                throw;
            }
        }

        // 初始化文件流
        private void InitializeFileStream(string filePath)
        {
            // 确保文件不存在（通过循环查找下一个可用序号）
            int retryIndex = _currentFileIndex;
            while (File.Exists(filePath))
            {
                retryIndex++;
                filePath = Path.Combine(_config.LogDirectory,
                    string.Format(_config.LogFileNameFormat, DateTime.Now, retryIndex));
            }
            _currentFileIndex = retryIndex; // 更新当前序号

            _fileStream?.Dispose();
            _fileStream = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite,
                _config.FileBufferSize,
                useAsync: true);

            _bufferOffset = 0;
        }

        // 写入文件
        private void WriteToFile(LogMessage message)
        {
            if (_isDisposed) return;

            try
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
            catch (Exception ex)
            {
                var errorMessage = new LogMessage(
                    DateTime.Now,
                    LogLevel.Error,
                    Encoding.UTF8.GetBytes($"日志写入失败: {ex.Message}"),
                    Environment.CurrentManagedThreadId,
                    Thread.CurrentThread.Name);

                WriteToConsoleDirect(errorMessage);
                ResetFileWriter();
                throw;
            }
        }

        // 内存映射文件写入
        private void WriteToMemoryMappedFile(LogMessage message)
        {
            lock (_mmfSwitchLock)
            {
                if (_isDisposed || _mapView == IntPtr.Zero || !_safeMapHandle.IsInvalid)
                {
                    SwitchToFileStreamMode();
                    WriteToFileStream(message);
                    return;
                }

                if (IsMemoryMappedFileSpaceLow())
                {
                    SwitchToFileStreamMode();
                    StartNewMemoryMappedFileTask();
                    WriteToFileStream(message);
                    return;
                }

                var formattedMessage = FormatMessage(message);
                EnsureMemoryMappedSpace(formattedMessage.Length);

                unsafe
                {
                    byte* mapPtr = (byte*)_mapView + _mapOffset;
                    formattedMessage.Span.CopyTo(new Span<byte>(mapPtr, formattedMessage.Length));
                    _mapOffset += formattedMessage.Length;
                }

                if (_mapOffset % (1024 * 1024) < formattedMessage.Length) // 每1MB刷新
                {
                    FlushViewOfFile(_mapView, (UIntPtr)formattedMessage.Length);
                }
            }
        }

        // 切换到文件流模式
        private void SwitchToFileStreamMode()
        {
            _useMemoryMappedFile = false;
            EnsureFileStreamInitialized();
        }

        // 确保文件流初始化
        private void EnsureFileStreamInitialized()
        {
            if (_fileStream == null || !_fileStream.CanWrite)
            {
                lock (_mmfSwitchLock)
                {
                    if (_fileStream == null || !_fileStream.CanWrite)
                    {
                        string currentFile = GetCurrentLogFileName();
                        InitializeFileStream(currentFile);
                    }
                }
            }
        }

        // 创建新内存映射文件任务
        private void StartNewMemoryMappedFileTask()
        {
            if (_createNewFileTask.IsCompleted)
            {
                _createNewFileTask = Task.Run(CreateNewMemoryMappedFileCore);
            }
        }

        // 创建新内存映射文件核心逻辑
        private void CreateNewMemoryMappedFileCore()
        {
            lock (_mmfSwitchLock)
            {
                if (_isDisposed) return;

                // 按日期重置序号
                if (DateTime.Now.Date > _lastFileDate)
                {
                    _currentFileIndex = 0;
                    _lastFileDate = DateTime.Now.Date;
                }
                _currentFileIndex++;

                string newFilePath = GetCurrentLogFileName();
                try
                {
                    InitializeMemoryMappedFile(newFilePath, _memoryMappedFileSize);
                    _useMemoryMappedFile = true;
                    _mapOffset = 0;
                    UnmapOldResources();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"创建新内存映射文件失败: {ex}");
                    _useMemoryMappedFile = false;
                }
            }
        }

        // 获取当前日志文件名
        private string GetCurrentLogFileName()
        {
            return Path.Combine(_config.LogDirectory,
                string.Format(_config.LogFileNameFormat, DateTime.Now, _currentFileIndex));
        }

        // 内存映射文件空间检查
        private bool IsMemoryMappedFileSpaceLow() =>
            (_memoryMappedFileSize - _mapOffset) < (_config.MemoryMappedThreadShould);
        /// <summary>
        /// 检查是否有写入指定文件的权限
        /// </summary>
        private bool CheckFileWritePermission(string filePath)
        {
            try
            {
                // 检查目录是否存在且可写
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    // 尝试创建目录
                    Directory.CreateDirectory(directory);
                }

                // 尝试创建一个临时文件进行权限测试
                string testFilePath = Path.Combine(directory, $"test_permission_{Guid.NewGuid()}.tmp");
                using (FileStream stream = File.Create(testFilePath, 1, FileOptions.DeleteOnClose))
                {
                    // 文件创建并关闭成功，说明有写入权限
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
        // 确保内存映射空间足够
        private void EnsureMemoryMappedSpace(int requiredSize)
        {
            if (_mapOffset + requiredSize > _memoryMappedFileSize)
            {
                FlushMemoryMappedFile();
                _mapOffset = 0;
            }
        }

        // 初始化内存映射文件
        private void InitializeMemoryMappedFile(string filePath, long size)
        {
            try
            {
                EnsureLogDirectoryExists(filePath);
                if (!CheckFileWritePermission(filePath)) throw new InvalidOperationException($"无写入权限: {filePath}");

                using (FileStream fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    fs.SetLength(size);
                }

                IntPtr fileHandle = CreateFile(
                    filePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                if (fileHandle == INVALID_HANDLE_VALUE)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"创建文件句柄失败: {filePath}");

                _safeFileHandle = new SafeFileHandle(fileHandle, true);

                IntPtr mapHandle = CreateFileMapping(
                    _safeFileHandle.DangerousGetHandle(),
                    IntPtr.Zero,
                    PAGE_READWRITE,
                    (uint)(size >> 32),
                    (uint)(size & 0xFFFFFFFF),
                    null);

                if (mapHandle == IntPtr.Zero)
                {
                    _safeFileHandle.Dispose();
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"创建内存映射失败: {filePath}");
                }

                _safeMapHandle = new SafeMapHandle();
                _safeMapHandle.Initialize(mapHandle);

                _mapView = MapViewOfFile(
                    _safeMapHandle.DangerousGetHandle(),
                    FILE_MAP_WRITE,
                    0,
                    0,
                    (UIntPtr)size);

                if (_mapView == IntPtr.Zero)
                {
                    _safeMapHandle.Dispose();
                    _safeFileHandle.Dispose();
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"映射视图失败: {filePath}");
                }

                _mapOffset = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化内存映射文件失败: {ex}");
                CleanupMemoryMappedResources();
                throw;
            }
        }

        // 清理内存映射资源
        private void CleanupMemoryMappedResources()
        {
            try
            {
                if (_mapView != IntPtr.Zero)
                {
                    FlushViewOfFile(_mapView, (UIntPtr)_mapOffset);
                    UnmapViewOfFile(_mapView);
                    _mapView = IntPtr.Zero;
                }
                _safeMapHandle?.Dispose();
                _safeFileHandle?.Dispose();
                _mapOffset = 0;
            }
            catch { /* 忽略清理异常 */ }
        }

        // 释放旧资源
        private void UnmapOldResources()
        {
            CleanupMemoryMappedResources();
        }

        // 刷新内存映射
        private void FlushMemoryMappedFile()
        {
            if (_mapView != IntPtr.Zero && _mapOffset > 0)
            {
                FlushViewOfFile(_mapView, (UIntPtr)_mapOffset);
            }
        }

        // 文件流写入
        private void WriteToFileStream(LogMessage message)
        {
            if (_fileStream == null)
            {
                lock (_mmfSwitchLock) { EnsureFileStreamInitialized(); }
            }

            var formattedMessage = FormatMessage(message);
            _writeLock.Wait();

            try
            {
                while (formattedMessage.Length > 0)
                {
                    int spaceLeft = _writeBuffer.Length - _bufferOffset;
                    if (spaceLeft == 0)
                    {
                        _fileStream.Write(_writeBuffer, 0, _writeBuffer.Length);
                        _bufferOffset = 0;
                    }

                    int copyLength = Math.Min(spaceLeft, formattedMessage.Length);
                    formattedMessage.Span.Slice(0, copyLength).CopyTo(_writeBuffer.AsSpan(_bufferOffset));
                    _bufferOffset += copyLength;
                    formattedMessage = formattedMessage.Slice(copyLength);
                }

                if (_bufferOffset >= _config.FlushInterval)
                {
                    _fileStream.Write(_writeBuffer, 0, _bufferOffset);
                    _bufferOffset = 0;
                }
            }
            finally { _writeLock.Release(); }
        }

        // 重置文件写入器
        private void ResetFileWriter()
        {
            lock (_mmfSwitchLock)
            {
                CleanupMemoryMappedResources();
                _fileStream?.Dispose();
                InitializeFileWriter();
            }
        }

        // IDisposable 实现
        public void Dispose()
        {
            lock (_mmfSwitchLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                try
                {
                    _cts.Cancel();
                    Task.WaitAll(_allTasks.ToArray(), TimeSpan.FromSeconds(10));

                    // 处理剩余日志
                    while (_logChannel.Reader.TryRead(out var msg))
                    {
                        if (msg.Level >= _config.FileLogLevel)
                        {
                            if (_useMemoryMappedFile && _mapView != IntPtr.Zero && _safeMapHandle.IsInvalid)
                            {
                                WriteToMemoryMappedFile(msg);
                            }
                            else
                            {
                                EnsureFileStreamInitialized();
                                WriteToFileStream(msg);
                            }
                        }
                    }

                    // 最终刷新
                    FlushMemoryMappedFile();
                    _fileStream?.Flush();
                }
                catch { /* 忽略终止异常 */ }
                finally { CleanupResources(); }
            }
        }

        // 清理所有资源
        private void CleanupResources()
        {
            CleanupMemoryMappedResources();
            _fileStream?.Dispose();
            _consoleQueue.CompleteAdding();
            _cts.Dispose();
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

        private void ProcessLogDirect(LogMessage logMessage)
        {
            if (_config.EnableConsoleWriting && logMessage.Level > _config.ConsoleLogLevel)
            {
                WriteToConsoleDirect(logMessage);
            }
            if (logMessage.Level > _config.FileLogLevel)
            {
                WriteToFile(logMessage);
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

                        if (_config.EnableConsoleWriting && message.Level >= _config.ConsoleLogLevel)
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
                if (_config.EnableConsoleColor)
                {
                    var originalColor = Console.ForegroundColor;
                    Console.ForegroundColor = GetConsoleColor(message.Level);
                    Console.Write(formattedStr);
                    Console.ForegroundColor = originalColor;
                }
                else
                {
                    Console.Write(formattedStr);
                }
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

        // 模板管理方法
        public void AddTemplate(LogTemplate template)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            _templates[template.Name] = template;
        }
        private void EnsureLogDirectoryExists(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
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
                ProcessLogDirect(logMessage);
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
                ProcessLogDirect(logMessage);
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

            // 重写 IsInvalid 属性，确保正确判断句柄有效性
            public override bool IsInvalid => handle == IntPtr.Zero || base.IsInvalid;

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
        private const uint OPEN_EXISTING = 3;  // 补充缺失的常量
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

// 日志配置类
public sealed class LoggerConfig
{
    public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Trace;
    public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
    public string LogDirectory { get; set; } = "Logs"; 
    public string LogFileNameFormat => "Log_{0:yyyyMMdd}_{1:D3}.dat";
    public bool EnableAsyncWriting { get; set; } = true;
    public bool EnableConsoleWriting { get; set; } = false;
    public int MaxQueueSize { get; set; } = int.MaxValue;
    public int BatchSize { get; set; } = 10_000;
    public int FlushInterval { get; set; } = 500;
    public bool EnableConsoleColor { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
    public int FileBufferSize { get; set; } = 64 * 1024;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public bool UseMemoryMappedFile { get; set; } = true;
    public long MemoryMappedFileSize { get; set; } = 1024 * 1024 * 1000; // 1000MB
    public long MemoryMappedThreadShould { get; set; } = 100 * 1024 * 1024;  // 100MB
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
