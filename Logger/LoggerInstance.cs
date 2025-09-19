using Microsoft.Win32.SafeHandles;
using Server.Common.Extensions;
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
        #region 单例模式
        // 单例模式实现，使用Lazy<T>确保线程安全的延迟初始化
        private static readonly Lazy<LoggerInstance> _instance = new Lazy<LoggerInstance>(
            () => new LoggerInstance(), LazyThreadSafetyMode.ExecutionAndPublication);

        // 提供全局访问点的静态属性
        public static LoggerInstance Instance => _instance.Value;
        #endregion

        #region 配置和状态相关成员
        private readonly LoggerConfig _config;                          // 日志记录器配置
        private readonly ConcurrentDictionary<string, LogTemplate> _templates = new ConcurrentDictionary<string, LogTemplate>(); // 日志模板缓存
        private readonly Channel<LogMessage> _logChannel;               // 用于异步日志处理的通道
        private readonly CancellationTokenSource _cts = new CancellationTokenSource(); // 用于取消操作的令牌源
        private readonly Task[] _processingTasks;                       // 日志处理任务数组
        private readonly List<Task> _allTasks = new List<Task>();      // 所有运行中任务的集合
        private bool _isDisposed;                                        // 资源释放状态标志
        private volatile bool _isAsyncWrite;                             // 异步写入标志，使用volatile确保多线程可见性
        #endregion
        public int GetLogChannelCount() => _logChannel.Reader.Count;
        public LoggerConfig GetLoggerConfig() => _config;
        #region 文件写入相关成员
        private FileStream _fileStream;                                  // 用于写入日志的文件流
        private readonly byte[] _writeBuffer;                            // 写入缓冲区
        private int _bufferOffset;                                       // 缓冲区偏移量
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1); // 写入操作的信号量锁
        private readonly object _mmfSwitchLock = new object();           // 内存映射文件切换锁
        #endregion

        #region 内存映射文件相关成员
        private IntPtr _mapView = IntPtr.Zero;                           // 内存映射视图指针
        private long _mapOffset;                                         // 内存映射偏移量
        private volatile bool _useMemoryMappedFile;                      // 是否使用内存映射文件标志
        private readonly long _memoryMappedFileSize;                     // 内存映射文件大小
        private SafeFileHandle _safeFileHandle;                          // 安全文件句柄
        private SafeMapHandle _safeMapHandle;                            // 安全映射句柄
        private Task _createNewFileTask = Task.CompletedTask;            // 记录当前创建新文件的任务
        private int _currentFileIndex = 0;                               // 当前文件索引
        private DateTime _lastFileDate = DateTime.MinValue;              // 最后一个文件的日期
        #endregion

        #region 性能监控相关成员
        private readonly Counter _totalLogsProcessed = new Counter();    // 处理的日志总数计数器
        private readonly Counter _queueFullCounter = new Counter();      // 队列满计数器
        private readonly System.Diagnostics.Stopwatch _performanceWatch = System.Diagnostics.Stopwatch.StartNew(); // 性能计时器
        #endregion

        #region 控制台输出相关成员
        private readonly BlockingCollection<LogMessage> _consoleQueue = new BlockingCollection<LogMessage>(); // 控制台输出队列
        private readonly Thread _consoleThread;                              // 控制台输出线程
        #endregion

        #region 构造函数
        // 私有构造函数
        private LoggerInstance() : this(new LoggerConfig()) { }

        public LoggerInstance(LoggerConfig config)
        {
            // 设置控制台输出编码为UTF-8，确保中文正常显示
            Console.OutputEncoding = Encoding.UTF8;

            // 初始化配置，若配置为空则抛出异常
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _writeBuffer = new byte[_config.FileBufferSize];                // 初始化文件写入缓冲区
            _useMemoryMappedFile = _config.UseMemoryMappedFile;            // 是否启用内存映射文件
            _memoryMappedFileSize = _config.MemoryMappedFileSize;          // 内存映射文件大小
            _isAsyncWrite = _config.EnableAsyncWriting;                    // 是否启用异步写入模式

            // 初始化日志通道，设置最大队列长度和溢出处理策略
            _logChannel = Channel.CreateBounded<LogMessage>(new BoundedChannelOptions(_config.MaxQueueSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,             // 队列满时丢弃最旧的日志
                SingleWriter = false,                                      // 允许多个写入者
                SingleReader = false                                       // 允许多个读取者
            });
            Console.WriteLine($"current maxqueue num {_config.MaxQueueSize}");

            // 添加默认日志模板，定义日志的基本格式
            AddTemplate(new LogTemplate
            {
                Name = "Default",
                Template = "{Timestamp} [{Level}] [{ThreadId}] {Message}{Exception}{Properties}",
                Level = LogLevel.Information,
                IncludeException = true
            });

            // 初始化文件写入器，创建日志文件并设置相关资源
            InitializeFileWriter();

            if (_isAsyncWrite)
            {
                // 启动多个日志处理任务，利用多核CPU并行处理日志
                _processingTasks = new Task[_config.MaxDegreeOfParallelism];
                for (int i = 0; i < _config.MaxDegreeOfParallelism; i++)
                {
                    _processingTasks[i] = Task.Factory.StartNew(
                        ProcessLogQueueAsync,                              // 异步处理日志队列的方法
                        _cts.Token,                                        // 取消令牌
                        TaskCreationOptions.LongRunning,                   // 标记为长时间运行的任务
                        TaskScheduler.Default);                            // 使用默认任务调度器

                    _allTasks.Add(_processingTasks[i]);                   // 将任务添加到监控列表
                }

                // 启动专用的控制台输出线程，避免阻塞日志处理
                _consoleThread = new Thread(ProcessConsoleQueue) { IsBackground = true };
                _consoleThread.Start();

                // 启动性能监控任务，定期收集和报告日志系统性能指标
                _allTasks.Add(Task.Factory.StartNew(MonitorPerformance, _cts.Token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default));
            }
        }
        #endregion

        #region 文件写入器初始化
        // 初始化文件写入器（核心逻辑）
        private void InitializeFileWriter()
        {
            try
            {
                // 确保日志目录存在，不存在则创建
                EnsureLogDirectoryExists(_config.LogDirectory);

                // 获取当前日期时间，用于生成文件名
                DateTime now = DateTime.Now;
                string currentDate = now.ToString("yyyyMMdd");
                _lastFileDate = now.Date; // 记录当前日期，用于判断是否需要重置文件索引

                // 如果日期变更，重置文件索引（每天从0开始生成新文件）
                if (currentDate != _lastFileDate.ToString("yyyyMMdd"))
                {
                    _currentFileIndex = 0;
                    _lastFileDate = now.Date;
                }

                string baseFileName;
                // 生成唯一的文件名：尝试递增索引直到找到不存在的文件
                do
                {
                    baseFileName = Path.Combine(_config.LogDirectory,
                        string.Format(_config.LogFileNameFormat, now, _currentFileIndex));
                    _currentFileIndex++; // 递增索引尝试下一个文件名
                } while (File.Exists(baseFileName));

                // 由于循环中多递增了一次，需要回退到实际使用的索引
                _currentFileIndex--;
                baseFileName = Path.Combine(_config.LogDirectory,
                    string.Format(_config.LogFileNameFormat, now, _currentFileIndex));

                // 根据配置选择初始化方式：内存映射文件(Windows优化)或普通文件流
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
                // 初始化失败时记录错误并释放资源
                Console.WriteLine($"初始化文件写入器失败: {ex}");
                Dispose();
                throw;
            }
        }

        // 初始化内存映射文件
        private void InitializeMemoryMappedFile(string filePath, long size)
        {
            try
            {
                // 确保日志目录存在
                EnsureLogDirectoryExists(filePath);

                // 检查文件写入权限
                if (!CheckFileWritePermission(filePath))
                    throw new InvalidOperationException($"无写入权限: {filePath}");

                // 创建并设置文件大小（预先分配空间）
                using (FileStream fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    fs.SetLength(size);
                }

                // 创建文件句柄（使用Win32 API）
                IntPtr fileHandle = CreateFile(
                    filePath,
                    GENERIC_READ | GENERIC_WRITE,         // 读写权限
                    FILE_SHARE_READ | FILE_SHARE_WRITE,  // 允许多进程共享读写
                    IntPtr.Zero,
                    OPEN_EXISTING,                       // 打开已存在的文件
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);

                // 检查文件句柄是否创建成功
                if (fileHandle == INVALID_HANDLE_VALUE)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"创建文件句柄失败: {filePath}");

                // 封装为安全句柄以便自动资源管理
                _safeFileHandle = new SafeFileHandle(fileHandle, true);

                // 创建文件映射对象
                IntPtr mapHandle = CreateFileMapping(
                    _safeFileHandle.DangerousGetHandle(),
                    IntPtr.Zero,
                    PAGE_READWRITE,                     // 读写访问权限
                    (uint)(size >> 32),                 // 文件大小高位
                    (uint)(size & 0xFFFFFFFF),          // 文件大小低位
                    null);

                // 检查映射对象是否创建成功
                if (mapHandle == IntPtr.Zero)
                {
                    _safeFileHandle.Dispose();
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"创建内存映射失败: {filePath}");
                }

                // 初始化安全映射句柄
                _safeMapHandle = new SafeMapHandle();
                _safeMapHandle.Initialize(mapHandle);

                // 映射视图（获取可直接访问的内存指针）
                _mapView = MapViewOfFile(
                    _safeMapHandle.DangerousGetHandle(),
                    FILE_MAP_WRITE,                     // 写入访问权限
                    0,                                  // 文件偏移高位
                    0,                                  // 文件偏移低位
                    (UIntPtr)size);                     // 映射大小

                // 检查视图映射是否成功
                if (_mapView == IntPtr.Zero)
                {
                    _safeMapHandle.Dispose();
                    _safeFileHandle.Dispose();
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"映射视图失败: {filePath}");
                }

                // 初始化映射偏移量（从文件起始位置开始写入）
                _mapOffset = 0;
            }
            catch (Exception ex)
            {
                // 记录错误并清理资源
                Console.WriteLine($"初始化内存映射文件失败: {ex}");
                CleanupMemoryMappedResources();
                throw;
            }
        }

        // 初始化文件流
        private void InitializeFileStream(string filePath)
        {
            // 再次确认文件是否存在（避免并发创建冲突）
            // 通过递增序号找到下一个可用的文件名
            int retryIndex = _currentFileIndex;
            while (File.Exists(filePath))
            {
                retryIndex++;
                filePath = Path.Combine(_config.LogDirectory,
                    string.Format(_config.LogFileNameFormat, DateTime.Now, retryIndex));
            }
            _currentFileIndex = retryIndex; // 更新当前文件索引为可用值

            // 释放之前的文件流资源
            _fileStream?.Dispose();

            // 创建新的文件流，使用异步模式提高性能
            _fileStream = new FileStream(
                filePath,                        // 文件路径
                FileMode.CreateNew,              // 创建新文件（不存在时），存在则抛出异常
                FileAccess.Write,                // 只写访问
                FileShare.ReadWrite,             // 允许其他进程读取和写入
                _config.FileBufferSize,          // 缓冲区大小配置
                useAsync: true);                 // 启用异步IO操作

            _bufferOffset = 0;                   // 重置缓冲区偏移量（从0开始写入）
        }
        #endregion

        #region 日志写入方法
        // 写入文件
        private void WriteToFile(LogMessage message)
        {
            // 检查是否已释放资源，防止在销毁后继续写入
            if (_isDisposed) return;

            try
            {
                // 根据配置选择写入方式：内存映射文件或普通文件流
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
                // 发生异常时，创建一个错误日志消息
                var errorMessage = new LogMessage(
                    DateTime.Now,
                    LogLevel.Error,
                    Encoding.UTF8.GetBytes($"日志写入失败: {ex.Message}"),
                    Environment.CurrentManagedThreadId,
                    Thread.CurrentThread.Name);

                // 直接输出错误信息到控制台（不经过队列）
                WriteToConsoleDirect(errorMessage);

                // 重置文件写入器，尝试恢复写入功能
                ResetFileWriter();

                // 重新抛出异常，由上层决定如何处理
                throw;
            }
        }

        // 内存映射文件写入
        private void WriteToMemoryMappedFile(LogMessage message)
        {
            // 使用专用锁确保内存映射文件操作的线程安全
            lock (_mmfSwitchLock)
            {
                // 检查资源状态：如果已释放或映射无效，则切换到文件流模式
                if (_isDisposed || _mapView == IntPtr.Zero || !_safeMapHandle.IsInvalid)
                {
                    SwitchToFileStreamMode();           // 切换到普通文件流模式
                    WriteToFileStream(message);         // 使用文件流写入当前消息
                    return;
                }

                // 检查内存映射文件剩余空间是否不足
                if (IsMemoryMappedFileSpaceLow())
                {
                    SwitchToFileStreamMode();           // 临时切换到文件流模式
                    StartNewMemoryMappedFileTask();     // 异步创建新的内存映射文件
                    WriteToFileStream(message);         // 使用文件流写入当前消息
                    return;
                }

                // 格式化日志消息为字节数组
                var formattedMessage = FormatMessage(message);
                // 确保内存映射文件有足够空间写入消息
                if (!EnsureMemoryMappedSpace(formattedMessage.Length))
                {
                    WriteToFileStream(message);         // 使用文件流写入当前消息
                    return;
                }

                // 使用不安全代码块直接操作内存指针，提高写入性能
                unsafe
                {
                    // 计算写入位置：基址 + 当前偏移量
                    byte* mapPtr = (byte*)_mapView + _mapOffset;
                    // 直接将消息内容复制到内存映射区域
                    formattedMessage.Span.CopyTo(new Span<byte>(mapPtr, formattedMessage.Length));
                    // 更新偏移量，指向下一个写入位置
                    _mapOffset += formattedMessage.Length;
                }

                // 每写入约1MB数据时刷新到磁盘，平衡性能和数据安全性
                if (_mapOffset % (1024 * 1024) < formattedMessage.Length)
                {
                    FlushViewOfFile(_mapView, (UIntPtr)formattedMessage.Length);
                }
            }
        }

        // 切换到文件流模式
        private void SwitchToFileStreamMode()
        {
            // 将写入模式切换为普通文件流模式
            _useMemoryMappedFile = false;

            // 确保文件流已初始化且可用
            // 该方法会检查文件流状态，必要时重新创建文件流
            EnsureFileStreamInitialized();
        }

        // 确保文件流初始化
        private void EnsureFileStreamInitialized()
        {
            // 检查文件流是否为null或不可写
            if (_fileStream == null || !_fileStream.CanWrite)
            {
                // 使用双重检查锁定模式确保线程安全
                lock (_mmfSwitchLock)
                {
                    // 再次验证，防止其他线程在等待锁时已初始化
                    if (_fileStream == null || !_fileStream.CanWrite)
                    {
                        // 获取当前应使用的日志文件名
                        string currentFile = GetCurrentLogFileName();

                        // 重新初始化文件流，确保写入操作可以继续
                        InitializeFileStream(currentFile);
                    }
                }
            }
        }

        // 创建新内存映射文件任务
        private void StartNewMemoryMappedFileTask()
        {
            // 检查是否有未完成的创建任务，避免重复创建
            if (_createNewFileTask.IsCompleted)
            {
                // 异步执行创建新内存映射文件的核心逻辑
                _createNewFileTask = Task.Run(CreateNewMemoryMappedFileCore);
            }
        }

        // 创建新内存映射文件核心逻辑
        private void CreateNewMemoryMappedFileCore()
        {
            // 使用专用锁确保线程安全，防止多个线程同时创建内存映射文件
            lock (_mmfSwitchLock)
            {
                // 检查是否已释放资源，防止在销毁后继续操作
                if (_isDisposed) return;

                // 检查是否需要按日期重置文件索引（每天从0开始生成新文件）
                if (DateTime.Now.Date > _lastFileDate)
                {
                    _currentFileIndex = 0;
                    _lastFileDate = DateTime.Now.Date;
                }
                // 递增文件索引，准备创建新文件
                _currentFileIndex++;

                // 获取新的日志文件路径
                string newFilePath = GetCurrentLogFileName();

                try
                {
                    // 初始化新的内存映射文件
                    InitializeMemoryMappedFile(newFilePath, _memoryMappedFileSize);

                    // 启用内存映射文件模式
                    _useMemoryMappedFile = true;

                    // 重置内存映射偏移量（从文件起始位置开始写入）
                    _mapOffset = 0;

                    // 释放旧的内存映射资源
                    UnmapOldResources();
                }
                catch (Exception ex)
                {
                    // 记录创建失败的错误信息
                    Console.WriteLine($"创建新内存映射文件失败: {ex}");

                    // 创建失败时切换回文件流模式
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

        // 确保内存映射空间足够
        private bool EnsureMemoryMappedSpace(int requiredSize)
        {
            // 检查当前内存映射文件剩余空间是否足够写入消息
            return (_mapOffset + requiredSize < _memoryMappedFileSize);
        }

        // 清理内存映射资源
        private void CleanupMemoryMappedResources()
        {
            try
            {
                // 检查内存映射视图是否存在
                if (_mapView != IntPtr.Zero)
                {
                    // 将内存中的数据刷新到磁盘
                    FlushViewOfFile(_mapView, (UIntPtr)_mapOffset);

                    // 解除内存映射，释放虚拟地址空间
                    UnmapViewOfFile(_mapView);

                    // 重置视图指针，标记为未映射状态
                    _mapView = IntPtr.Zero;
                }

                // 释放安全映射句柄资源
                _safeMapHandle?.Dispose();

                // 释放安全文件句柄资源
                _safeFileHandle?.Dispose();

                // 重置映射偏移量
                _mapOffset = 0;
            }
            catch
            {
                // 忽略清理过程中可能出现的异常
                // 确保资源释放操作不会中断程序运行
            }
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
            // 确保文件流已初始化
            if (_fileStream == null)
            {
                lock (_mmfSwitchLock) { EnsureFileStreamInitialized(); }
            }

            // 格式化日志消息为字节数组
            var formattedMessage = FormatMessage(message);

            // 获取写入锁，确保线程安全
            _writeLock.Wait();

            try
            {
                // 循环处理完整的消息内容（可能需要分块写入缓冲区）
                while (formattedMessage.Length > 0)
                {
                    // 计算当前缓冲区剩余空间
                    int spaceLeft = _writeBuffer.Length - _bufferOffset;

                    // 如果缓冲区已满，则将内容写入文件流
                    if (spaceLeft == 0)
                    {
                        _fileStream.Write(_writeBuffer, 0, _writeBuffer.Length);
                        _bufferOffset = 0; // 重置缓冲区偏移量
                    }

                    // 确定本次可复制的最大长度
                    int copyLength = Math.Min(spaceLeft, formattedMessage.Length);

                    // 将消息内容复制到缓冲区
                    formattedMessage.Span.Slice(0, copyLength).CopyTo(_writeBuffer.AsSpan(_bufferOffset));

                    // 更新缓冲区偏移量和剩余待处理消息长度
                    _bufferOffset += copyLength;
                    formattedMessage = formattedMessage.Slice(copyLength);
                }

                // 当缓冲区内容达到刷新阈值时，将内容写入文件流
                if (_bufferOffset >= _config.FlushInterval)
                {
                    _fileStream.Write(_writeBuffer, 0, _bufferOffset);
                    _bufferOffset = 0; // 重置缓冲区偏移量
                }
            }
            finally
            {
                // 释放写入锁，允许其他线程继续写入
                _writeLock.Release();
            }
        }

        // 重置文件写入器
        private void ResetFileWriter()
        {
            // 使用专用锁确保线程安全，防止多个线程同时重置文件写入器
            lock (_mmfSwitchLock)
            {
                // 清理内存映射文件相关资源
                CleanupMemoryMappedResources();

                // 释放当前文件流资源
                _fileStream?.Dispose();

                // 重新初始化文件写入器，创建新的文件流或内存映射文件
                InitializeFileWriter();
            }
        }
        #endregion

        #region 资源释放
        // IDisposable 实现
        public void Dispose()
        {
            // 使用专用锁确保线程安全，防止多个线程同时执行释放操作
            lock (_mmfSwitchLock)
            {
                // 检查是否已释放，避免重复释放
                if (_isDisposed) return;
                _isDisposed = true;

                try
                {
                    // 取消所有正在执行的任务
                    _cts.Cancel();

                    // 等待所有任务完成（最多等待10秒）
                    Task.WaitAll(_allTasks.ToArray(), TimeSpan.FromSeconds(10));

                    // 处理队列中剩余的日志消息
                    while (_logChannel.Reader.TryRead(out var msg))
                    {
                        // 只处理级别符合配置的日志
                        if (msg.Level >= _config.FileLogLevel)
                        {
                            // 根据当前模式选择写入方式
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

                    // 最终刷新操作，确保所有缓冲数据写入磁盘
                    FlushMemoryMappedFile();
                    _fileStream?.Flush();
                }
                catch
                {
                    // 忽略终止过程中可能出现的异常
                    // 确保资源释放操作不会中断程序运行
                }
                finally
                {
                    // 清理所有资源
                    CleanupResources();
                }
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
        #endregion

        #region 日志处理
        // 入队日志消息
        private void EnqueueLogMessage(LogMessage message)
        {
            // 检查日志记录器是否已释放，防止在释放后继续处理日志
            if (_isDisposed) return;

            // 尝试将日志消息写入通道队列
            if (!_logChannel.Writer.TryWrite(message))
            {
                // 队列已满，记录丢弃次数
                _queueFullCounter.Increment();

                // 每丢弃1000条日志时记录一次警告
                if (_queueFullCounter.Value % 1000 == 0)
                {
                    var warningMsg = new LogMessage(
                        DateTime.Now,
                        LogLevel.Warning,
                        Encoding.UTF8.GetBytes($"日志队列已满，已丢弃 {_queueFullCounter.Value} 条日志"),
                        Environment.CurrentManagedThreadId,
                        Thread.CurrentThread.Name);

                    // 直接写入控制台，不经过队列，确保警告信息能被看到
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
        #endregion

        #region 消息格式化
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
        #endregion

        #region 控制台输出
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
        #endregion

        #region 性能监控
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
        #endregion

        #region 模板管理
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
        #endregion

        #region 核心日志方法
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
        #endregion

        #region 快捷日志方法
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
        #endregion

        #region Win32 API封装
        // 安全句柄封装类
        private sealed class SafeMapHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            // 导入Win32 API函数，用于关闭内核对象句柄
            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool CloseHandle(IntPtr handle);

            // 构造函数，设置为需要释放资源
            public SafeMapHandle() : base(true) { }

            // 公共方法，用于初始化安全句柄
            public void Initialize(IntPtr handle)
            {
                base.SetHandle(handle);
            }

            // 重写IsInvalid属性，判断句柄是否无效
            // 当句柄为IntPtr.Zero或基类认为无效时，句柄无效
            public override bool IsInvalid => handle == IntPtr.Zero || base.IsInvalid;

            // 重写资源释放方法，调用Win32 API关闭句柄
            protected override bool ReleaseHandle()
            {
                return CloseHandle(handle);
            }
        }

        // Win32 API P/Invoke声明（用于内存映射文件和文件操作）
        /// <summary>
        /// 创建或打开文件/设备，并返回可用于访问该文件/设备的句柄
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,                 // 文件路径
            uint dwDesiredAccess,              // 访问模式（读/写/读写）
            uint dwShareMode,                  // 共享模式（读/写/无共享）
            IntPtr lpSecurityAttributes,       // 安全描述符（通常为IntPtr.Zero）
            uint dwCreationDisposition,        // 创建选项（如CREATE_NEW, OPEN_EXISTING）
            uint dwFlagsAndAttributes,         // 文件属性和标志
            IntPtr hTemplateFile);             // 模板文件句柄（通常为IntPtr.Zero）

        /// <summary>
        /// 创建文件映射对象，用于将文件内容映射到进程的地址空间
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateFileMapping(
            IntPtr hFile,                      // 文件句柄
            IntPtr lpFileMappingAttributes,    // 安全属性（通常为IntPtr.Zero）
            uint flProtect,                    // 保护模式（如PAGE_READWRITE）
            uint dwMaximumSizeHigh,            // 文件映射的最大大小（高位）
            uint dwMaximumSizeLow,             // 文件映射的最大大小（低位）
            string lpName);                    // 命名映射对象名称（可为null）

        /// <summary>
        /// 解除内存中文件映射视图的映射关系
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress); // 映射视图的基址

        /// <summary>
        /// 将文件映射对象的一部分映射到当前进程的地址空间
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(
            IntPtr hFileMappingObject,         // 文件映射对象句柄
            uint dwDesiredAccess,              // 访问类型（如FILE_MAP_WRITE）
            uint dwFileOffsetHigh,             // 文件偏移量（高位）
            uint dwFileOffsetLow,              // 文件偏移量（低位）
            UIntPtr dwNumberOfBytesToMap);     // 要映射的字节数

        /// <summary>
        /// 设置文件指针的位置（用于大文件操作）
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(
            IntPtr hFile,                      // 文件句柄
            long liDistanceToMove,             // 移动距离
            out long lpNewFilePointer,         // 新的文件指针位置
            uint dwMoveMethod);                // 移动方法（如FILE_BEGIN）

        /// <summary>
        /// 将文件的当前位置设置为文件末尾
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetEndOfFile(IntPtr hFile); // 文件句柄

        /// <summary>
        /// 关闭打开的对象句柄（如文件句柄、映射对象句柄等）
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject); // 要关闭的句柄

        /// <summary>
        /// 将内存映射视图中的数据刷新到磁盘
        /// </summary>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushViewOfFile(
            IntPtr lpBaseAddress,              // 映射视图的基址
            UIntPtr dwNumberOfBytesToFlush);   // 要刷新的字节数

        // Windows API常量定义（用于内存映射文件相关操作）
        private const uint GENERIC_WRITE = 0x40000000;    // 通用写访问权限（用于文件打开标志）
        private const uint FILE_SHARE_READ = 0x00000001;   // 允许其他进程读取文件（文件共享标志）
        private const uint GENERIC_READ = 0x80000000;     // 通用读访问权限（用于文件打开标志）
        private const uint FILE_SHARE_WRITE = 0x00000002;  // 允许其他进程写入文件（文件共享标志）
        private const uint OPEN_ALWAYS = 4;                // 始终打开文件：若存在则打开，否则创建（文件打开方式）
        private const uint OPEN_EXISTING = 3;              // 仅打开已存在的文件：若不存在则失败（文件打开方式）
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;   // 普通文件属性（无特殊属性）
        private const uint PAGE_READWRITE = 0x04;          // 内存页保护属性：允许读写访问（内存映射标志）
        private const uint FILE_MAP_WRITE = 0x0002;        // 内存映射访问权限：允许写入映射区域
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1); // 无效句柄值（用于错误判断）
        #endregion

        #region 辅助类
        // 原子计数器
        private sealed class Counter
        {
            private long _value;
            public long Value => _value;

            public void Increment() => Interlocked.Increment(ref _value);
            public void Add(long amount) => Interlocked.Add(ref _value, amount);
        }
        #endregion

        #region 路径权限检查
        private void EnsureLogDirectoryExists(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
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
        #endregion
    }
}