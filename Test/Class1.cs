//using System;
//using System.Buffers;
//using System.Buffers.Text;
//using System.Collections.Concurrent;
//using System.Diagnostics;
//using System.IO;
//using System.IO.MemoryMappedFiles;
//using System.IO.Pipelines;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;
//using System.Text;
//using System.Threading;
//using System.Threading.Channels;
//using System.Threading.Tasks;

//// ==========================
//// 系统检测与配置模块
//// ==========================

//public enum OperatingSystemType
//{
//    Windows,
//    Linux,
//    macOS,
//    Unknown
//}

//public enum DiskType
//{
//    HDD,
//    SSD,
//    NVMe,
//    NVDIMM,
//    Unknown
//}

//public enum HardwareProfile
//{
//    Mobile,
//    Standard,
//    HighPerformance,
//    ExtremePerformance
//}

//public enum LogLevel
//{
//    Trace,
//    Debug,
//    Information,
//    Warning,
//    Error,
//    Critical
//}

//public class SystemDetector
//{
//    public static OperatingSystemType DetectSystemType()
//    {
//        if (OperatingSystem.IsWindows()) return OperatingSystemType.Windows;
//        if (OperatingSystem.IsLinux()) return OperatingSystemType.Linux;
//        if (OperatingSystem.IsMacOS()) return OperatingSystemType.macOS;
//        return OperatingSystemType.Unknown;
//    }

//    public static DiskType DetectDiskType(string logPath)
//    {
//        try
//        {
//            string testFile = Path.Combine(logPath, "disk_test.tmp");
//            using var fs = new FileStream(testFile, FileMode.Create, FileAccess.ReadWrite,
//                FileShare.None, 4096, FileOptions.DeleteOnClose);

//            var random = new Random();
//            byte[] buffer = new byte[4096];
//            random.NextBytes(buffer);

//            Stopwatch sw = Stopwatch.StartNew();
//            for (int i = 0; i < 100; i++)
//            {
//                long pos = random.Next(0, 1024 * 1024);
//                fs.Seek(pos, SeekOrigin.Begin);
//                fs.Write(buffer, 0, buffer.Length);
//            }
//            sw.Stop();

//            double avgMsPerWrite = sw.Elapsed.TotalMilliseconds / 100;

//            if (avgMsPerWrite > 5) return DiskType.HDD;
//            if (avgMsPerWrite > 0.1) return DiskType.SSD;
//            if (avgMsPerWrite < 0.1) return DiskType.NVMe;

//            try
//            {
//                using var mmf = MemoryMappedFile.CreateFromFile(fs.SafeFileHandle, null, 0, MemoryMappedFileAccess.ReadWrite);
//                return DiskType.NVDIMM;
//            }
//            catch { }

//            return DiskType.Unknown;
//        }
//        catch { return DiskType.Unknown; }
//    }

//    public static HardwareProfile DetectHardwareProfile()
//    {
//        long totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
//        int processorCount = Environment.ProcessorCount;

//        if (processorCount <= 4 || totalMemory < 8 * 1024 * 1024 * 1024)
//            return HardwareProfile.Mobile;

//        if (processorCount <= 16 || totalMemory < 64 * 1024 * 1024 * 1024)
//            return HardwareProfile.Standard;

//        if (processorCount <= 64 || totalMemory < 512 * 1024 * 1024 * 1024)
//            return HardwareProfile.HighPerformance;

//        return HardwareProfile.ExtremePerformance;
//    }
//}

//// ==========================
//// 平台特定IO实现
//// ==========================

//public interface IPlatformIO : IAsyncDisposable
//{
//    ValueTask WriteAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken = default);
//    ValueTask FlushAsync(CancellationToken cancellationToken = default);
//    ValueTask PreallocateFileAsync(long size, CancellationToken cancellationToken = default);
//}

//// Windows 实现
//public sealed class WindowsIO : IPlatformIO
//{
//    private readonly FileStream _fileStream;
//    private readonly PipeWriter _pipeWriter;

//    public WindowsIO(string path)
//    {
//        _fileStream = new FileStream(
//            path,
//            new FileStreamOptions
//            {
//                Access = FileAccess.Write,
//                Mode = FileMode.Append,
//                Share = FileShare.Read,
//                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
//                BufferSize = 64 * 1024
//            });

//        _pipeWriter = PipeWriter.Create(_fileStream, new StreamPipeWriterOptions(leaveOpen: true));
//    }

//    public async ValueTask WriteAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken = default)
//    {
//        foreach (var segment in data)
//        {
//            var memory = _pipeWriter.GetMemory(segment.Length);
//            segment.CopyTo(memory.Span);
//            _pipeWriter.Advance(segment.Length);
//        }

//        await _pipeWriter.FlushAsync(cancellationToken);
//    }

//    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
//        => _pipeWriter.FlushAsync(cancellationToken);

//    public async ValueTask PreallocateFileAsync(long size, CancellationToken cancellationToken = default)
//    {
//        await _fileStream.SetLengthAsync(size, cancellationToken);
//    }

//    public async ValueTask DisposeAsync()
//    {
//        await _pipeWriter.CompleteAsync();
//        await _fileStream.DisposeAsync();
//    }
//}

//// Windows DirectIO 实现
//public sealed class WindowsDirectIO : IPlatformIO, IDisposable
//{
//    private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
//    private const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;
//    private const uint GENERIC_WRITE = 0x40000000;
//    private const uint CREATE_ALWAYS = 2;

//    [DllImport("kernel32.dll", SetLastError = true)]
//    private static extern IntPtr CreateFile(
//        string lpFileName,
//        uint dwDesiredAccess,
//        uint dwShareMode,
//        IntPtr lpSecurityAttributes,
//        uint dwCreationDisposition,
//        uint dwFlagsAndAttributes,
//        IntPtr hTemplateFile);

//    [DllImport("kernel32.dll", SetLastError = true)]
//    [return: MarshalAs(MarshalType.Bool)]
//    private static extern bool WriteFile(
//        IntPtr hFile,
//        byte[] lpBuffer,
//        uint nNumberOfBytesToWrite,
//        out uint lpNumberOfBytesWritten,
//        IntPtr lpOverlapped);

//    [DllImport("kernel32.dll", SetLastError = true)]
//    [return: MarshalAs(MarshalType.Bool)]
//    private static extern bool FlushFileBuffers(IntPtr hFile);

//    [DllImport("kernel32.dll", SetLastError = true)]
//    [return: MarshalAs(MarshalType.Bool)]
//    private static extern bool CloseHandle(IntPtr hObject);

//    private readonly IntPtr _fileHandle;
//    private readonly string _filePath;

//    public WindowsDirectIO(string filePath)
//    {
//        _filePath = filePath;
//        _fileHandle = CreateFile(
//            filePath,
//            GENERIC_WRITE,
//            0,
//            IntPtr.Zero,
//            CREATE_ALWAYS,
//            FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH,
//            IntPtr.Zero);

//        if (_fileHandle == IntPtr.Zero)
//            throw new IOException($"Failed to open file: {filePath}", Marshal.GetLastWin32Error());
//    }

//    public unsafe Task WriteAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken = default)
//    {
//        if (cancellationToken.IsCancellationRequested)
//            return Task.FromCanceled(cancellationToken);

//        foreach (var segment in data)
//        {
//            fixed (byte* ptr = segment.Span)
//            {
//                if (!WriteFile(_fileHandle, segment.ToArray(), (uint)segment.Length, out _, IntPtr.Zero))
//                {
//                    throw new IOException("WriteFile failed", Marshal.GetLastWin32Error());
//                }
//            }
//        }

//        return Task.CompletedTask;
//    }

//    public Task FlushAsync(CancellationToken cancellationToken = default)
//    {
//        if (!FlushFileBuffers(_fileHandle))
//            throw new IOException("FlushFileBuffers failed", Marshal.GetLastWin32Error());

//        return Task.CompletedTask;
//    }

//    public Task PreallocateFileAsync(long size, CancellationToken cancellationToken = default)
//    {
//        // 使用 SetFilePointerEx 和 SetEndOfFile 预分配空间
//        return Task.CompletedTask;
//    }

//    public void Dispose()
//    {
//        if (_fileHandle != IntPtr.Zero)
//        {
//            CloseHandle(_fileHandle);
//        }
//    }

//    public ValueTask DisposeAsync()
//    {
//        Dispose();
//        return ValueTask.CompletedTask;
//    }
//}

//// Linux io_uring 实现
//public sealed class LinuxIoUringWriter : IPlatformIO
//{
//    private const int IORING_OP_WRITE = 2;
//    private const int IORING_SETUP_SQPOLL = 0x0001;

//    [StructLayout(LayoutKind.Sequential)]
//    private struct io_uring_sqe
//    {
//        public uint opcode;
//        public uint flags;
//        public ushort ioprio;
//        public int fd;
//        public uint off_low;
//        public uint off_high;
//        public ulong addr;
//        public uint len;
//        public union
//        {
//            uint __pad2;
//        uint buf_index;
//    };
//    public union
//        {
//            uint __pad3;
//    uint buf_group;
//};
//public int rw_flags;
//public union
//{
//    ulong user_data;
//    long __pad4;
//}
//;
//public uint __pad5[3];
//    }

//    [StructLayout(LayoutKind.Sequential)]
//private struct io_uring_cqe
//{
//    public uint user_data;
//    public int res;
//    public uint flags;
//}

//[StructLayout(LayoutKind.Sequential)]
//private struct io_uring_params
//{
//    public uint sq_entries;
//    public uint cq_entries;
//    public uint flags;
//    public uint sq_thread_cpu;
//    public uint sq_thread_idle;
//    public uint features;
//    public uint wq_fd;
//    public uint pad[4];
//    public io_sqring_offsets sq_off;
//    public io_cqring_offsets cq_off;
//}

//[StructLayout(LayoutKind.Sequential)]
//private struct io_sqring_offsets
//{
//    public uint head;
//    public uint tail;
//    public uint ring_mask;
//    public uint ring_entries;
//    public uint flags;
//    public uint dropped;
//    public uint array;
//    public uint resv1;
//    public ulong resv2;
//}

//[StructLayout(LayoutKind.Sequential)]
//private struct io_cqring_offsets
//{
//    public uint head;
//    public uint tail;
//    public uint ring_mask;
//    public uint ring_entries;
//    public uint overflow;
//    public uint cqes;
//    public uint resv1;
//    public ulong resv2;
//}

//[DllImport("libc.so.6", SetLastError = true)]
//private static extern int io_uring_queue_init(uint entries, out IntPtr ring, uint flags);

//[DllImport("libc.so.6", SetLastError = true)]
//private static extern int io_uring_queue_exit(IntPtr ring);

//[DllImport("libc.so.6", SetLastError = true)]
//private static extern IntPtr io_uring_get_sqe(IntPtr ring);

//[DllImport("libc.so.6", SetLastError = true)]
//private static extern void io_uring_prep_write(IntPtr sqe, int fd, IntPtr buf, uint nbytes, ulong offset);

//[DllImport("libc.so.6", SetLastError = true)]
//private static extern int io_uring_submit(IntPtr ring);

//[DllImport("libc.so.6", SetLastError = true)]
//private static extern int io_uring_wait_cqe(IntPtr ring, out IntPtr cqe_ptr);

//[DllImport("libc.so.6", SetLastError = true)]
//private static extern void io_uring_cqe_seen(IntPtr ring, IntPtr cqe_ptr);

//private readonly IntPtr _ring;
//private readonly int _fd;
//private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

//public LinuxIoUringWriter(string path)
//{
//    _fd = System.IO.File.Open(path, FileMode.Append, FileAccess.Write).Handle.ToInt32();

//    if (io_uring_queue_init(1024, out _ring, 0) != 0)
//        throw new IOException("Failed to initialize io_uring");
//}

//public unsafe ValueTask WriteAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken = default)
//{
//    return new ValueTask(Task.Run(async () =>
//    {
//        await _semaphore.WaitAsync(cancellationToken);

//        try
//        {
//            foreach (var segment in data)
//            {
//                fixed (byte* ptr = segment.Span)
//                {
//                    var sqe = io_uring_get_sqe(_ring);
//                    io_uring_prep_write(sqe, _fd, (IntPtr)ptr, (uint)segment.Length, 0);
//                }
//            }

//            io_uring_submit(_ring);

//            // 等待所有 CQE 完成
//            for (int i = 0; i < data.Length; i++)
//            {
//                if (io_uring_wait_cqe(_ring, out var cqe_ptr) != 0)
//                    throw new IOException("io_uring_wait_cqe failed");

//                io_uring_cqe_seen(_ring, cqe_ptr);
//            }
//        }
//        finally
//        {
//            _semaphore.Release();
//        }
//    }, cancellationToken));
//}

//public ValueTask FlushAsync(CancellationToken cancellationToken = default)
//{
//    // 使用 fsync
//    return ValueTask.CompletedTask;
//}

//public ValueTask PreallocateFileAsync(long size, CancellationToken cancellationToken = default)
//{
//    // 使用 fallocate
//    return ValueTask.CompletedTask;
//}

//public ValueTask DisposeAsync()
//{
//    io_uring_queue_exit(_ring);
//    return ValueTask.CompletedTask;
//}
//}

//// ==========================
//// 高性能组件
//// ==========================

//// 零拷贝缓冲区写入器
//public sealed class UnsafeBufferWriter : IBufferWriter<byte>, IDisposable
//{
//    private byte[] _buffer;
//    private int _position;
//    private readonly int _initialCapacity;
//    private readonly ArrayPool<byte> _pool;

//    public UnsafeBufferWriter(int initialCapacity = 4096)
//    {
//        _initialCapacity = initialCapacity;
//        _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
//        _pool = ArrayPool<byte>.Shared;
//    }

//    public void Advance(int count)
//    {
//        if (count < 0 || _position + count > _buffer.Length)
//            throw new ArgumentOutOfRangeException(nameof(count));

//        _position += count;
//    }

//    public Memory<byte> GetMemory(int sizeHint = 0)
//    {
//        EnsureCapacity(sizeHint);
//        return _buffer.AsMemory(_position);
//    }

//    public Span<byte> GetSpan(int sizeHint = 0)
//    {
//        EnsureCapacity(sizeHint);
//        return _buffer.AsSpan(_position);
//    }

//    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _position);

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    public void Write(ReadOnlySpan<byte> source)
//    {
//        EnsureCapacity(source.Length);
//        source.CopyTo(_buffer.AsSpan(_position));
//        _position += source.Length;
//    }

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    public void WriteString(string value)
//    {
//        if (string.IsNullOrEmpty(value))
//            return;

//        int maxByteCount = Encoding.UTF8.GetMaxByteCount(value.Length);
//        EnsureCapacity(maxByteCount);

//        int byteCount = Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_position));
//        _position += byteCount;
//    }

//    private void EnsureCapacity(int additionalCapacity)
//    {
//        if (_position + additionalCapacity > _buffer.Length)
//        {
//            int newCapacity = Math.Max(_buffer.Length * 2, _position + additionalCapacity);
//            var newBuffer = _pool.Rent(newCapacity);

//            Array.Copy(_buffer, 0, newBuffer, 0, _position);
//            _pool.Return(_buffer);

//            _buffer = newBuffer;
//        }
//    }

//    public void Reset() => _position = 0;

//    public void Dispose()
//    {
//        _pool.Return(_buffer);
//        _buffer = null!;
//        _position = 0;
//    }
//}

//// 日志格式化器
//public static class LogFormatter
//{
//    private static readonly byte[] _timestampPrefix = Encoding.UTF8.GetBytes("[");
//    private static readonly byte[] _timestampSuffix = Encoding.UTF8.GetBytes("] ");
//    private static readonly byte[] _levelTrace = Encoding.UTF8.GetBytes("TRACE");
//    private static readonly byte[] _levelDebug = Encoding.UTF8.GetBytes("DEBUG");
//    private static readonly byte[] _levelInfo = Encoding.UTF8.GetBytes("INFO ");
//    private static readonly byte[] _levelWarn = Encoding.UTF8.GetBytes("WARN ");
//    private static readonly byte[] _levelError = Encoding.UTF8.GetBytes("ERROR");
//    private static readonly byte[] _levelCritical = Encoding.UTF8.GetBytes("CRITI");
//    private static readonly byte[] _colonSpace = Encoding.UTF8.GetBytes(": ");
//    private static readonly byte[] _newLine = Encoding.UTF8.GetBytes(Environment.NewLine);

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    public static void FormatLogEntry<TBufferWriter>(
//        TBufferWriter writer,
//        LogLevel level,
//        string message)
//        where TBufferWriter : IBufferWriter<byte>
//    {
//        // 写入时间戳
//        writer.Write(_timestampPrefix);
//        WriteTimestamp(writer, DateTimeOffset.UtcNow);
//        writer.Write(_timestampSuffix);

//        // 写入日志级别
//        writer.Write(GetLevelBytes(level));
//        writer.Write(_colonSpace);

//        // 写入消息内容
//        writer.WriteString(message);
//        writer.Write(_newLine);
//    }

//    private static void WriteTimestamp<TBufferWriter>(TBufferWriter writer, DateTimeOffset timestamp)
//        where TBufferWriter : IBufferWriter<byte>
//    {
//        Span<byte> buffer = stackalloc byte[28];
//        if (Utf8Formatter.TryFormat(timestamp, buffer, out int written, 'O'))
//        {
//            writer.Write(buffer.Slice(0, written));
//        }
//        else
//        {
//            writer.WriteString(timestamp.ToString("O"));
//        }
//    }

//    private static ReadOnlySpan<byte> GetLevelBytes(LogLevel level) => level switch
//    {
//        LogLevel.Trace => _levelTrace,
//        LogLevel.Debug => _levelDebug,
//        LogLevel.Information => _levelInfo,
//        LogLevel.Warning => _levelWarn,
//        LogLevel.Error => _levelError,
//        LogLevel.Critical => _levelCritical,
//        _ => _levelInfo
//    };
//}

//// 日志条目对象池
//public sealed class LogEntryPool
//{
//    private readonly ConcurrentBag<LogEntry> _pool = new();

//    public LogEntry Rent(ReadOnlyMemory<byte> data)
//    {
//        if (_pool.TryTake(out var entry))
//        {
//            entry.SetData(data);
//            return entry;
//        }

//        return new LogEntry(data, this);
//    }

//    public void Return(LogEntry entry)
//    {
//        entry.Reset();
//        _pool.Add(entry);
//    }
//}

//// 可复用的日志条目
//public sealed class LogEntry : IDisposable
//{
//    private readonly LogEntryPool _pool;
//    private ReadOnlyMemory<byte> _data;
//    private bool _isReturned;

//    public LogEntry(ReadOnlyMemory<byte> data, LogEntryPool pool)
//    {
//        _data = data;
//        _pool = pool;
//    }

//    public ReadOnlyMemory<byte> Data => _data;

//    internal void SetData(ReadOnlyMemory<byte> data)
//    {
//        _data = data;
//        _isReturned = false;
//    }

//    internal void Reset()
//    {
//        _data = default;
//    }

//    public void Dispose()
//    {
//        Return();
//    }

//    public void Return()
//    {
//        if (!_isReturned)
//        {
//            _isReturned = true;
//            _pool.Return(this);
//        }
//    }
//}

//// 线程亲和性管理器
//public sealed class ThreadAffinityManager
//{
//    private readonly bool _isSupported;
//    private readonly int _processorCount;

//    public ThreadAffinityManager()
//    {
//        _processorCount = Environment.ProcessorCount;
//        _isSupported = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
//    }

//    public void SetThreadAffinity(int threadIndex)
//    {
//        if (!_isSupported)
//            return;

//        var currentThread = Thread.CurrentThread;

//        if (OperatingSystem.IsWindows())
//        {
//            // Windows 线程亲和性设置
//            Process.GetCurrentProcess().ProcessorAffinity = new IntPtr(1L << (threadIndex % _processorCount));
//        }
//        else if (OperatingSystem.IsLinux())
//        {
//            // Linux 线程亲和性设置
//            // 此处省略具体实现
//        }

//        currentThread.Priority = ThreadPriority.Highest;
//    }
//}

//// ==========================
//// 配置类
//// ==========================

//public class LoggerConfig
//{
//    public int MaxQueueSize { get; set; } = 100_000;
//    public int FileBufferSize { get; set; } = 64 * 1024;
//    public long MemoryMappedFileSize { get; set; } = 1024 * 1024 * 100;
//    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
//    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);
//    public bool UseDirectIO { get; set; } = true;
//    public bool UseIoUring { get; set; } = true;
//}

//public class AdaptiveLoggerConfig : LoggerConfig
//{
//    public AdaptiveLoggerConfig()
//    {
//        var hardwareProfile = SystemDetector.DetectHardwareProfile();

//        switch (hardwareProfile)
//        {
//            case HardwareProfile.Mobile:
//                MaxQueueSize = 10_000;
//                FileBufferSize = 8 * 1024;
//                UseDirectIO = false;
//                UseIoUring = false;
//                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2);
//                break;

//            case HardwareProfile.Standard:
//                MaxQueueSize = 100_000;
//                FileBufferSize = 64 * 1024;
//                MaxDegreeOfParallelism = Environment.ProcessorCount;
//                break;

//            case HardwareProfile.HighPerformance:
//                MaxQueueSize = 1_000_000;
//                FileBufferSize = 256 * 1024;
//                MaxDegreeOfParallelism = Environment.ProcessorCount * 2;
//                break;

//            case HardwareProfile.ExtremePerformance:
//                MaxQueueSize = 10_000_000;
//                FileBufferSize = 1024 * 1024;
//                MaxDegreeOfParallelism = Environment.ProcessorCount * 4;
//                break;
//        }
//    }
//}

//// ==========================
//// 高性能日志系统核心
//// ==========================

//public sealed class SuperLogger : IAsyncDisposable
//{
//    private readonly LoggerConfig _config;
//    private readonly IPlatformIO _platformIO;
//    private readonly Channel<LogEntry> _logChannel;
//    private readonly Task[] _processingTasks;
//    private readonly CancellationTokenSource _cancellationTokenSource = new();
//    private readonly ThreadAffinityManager _threadAffinityManager = new();
//    private readonly LogEntryPool _logEntryPool = new();
//    private readonly Timer _flushTimer;
//    private readonly ActivitySource _activitySource = new("SuperLogger");

//    public SuperLogger(string logPath)
//    {
//        _config = new AdaptiveLoggerConfig();
//        _platformIO = CreatePlatformIO(logPath);

//        _logChannel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(_config.MaxQueueSize)
//        {
//            FullMode = BoundedChannelFullMode.DropOldest,
//            SingleReader = false,
//            AllowSynchronousContinuations = false
//        });

//        // 根据 CPU 核心数创建处理任务
//        int processorCount = Environment.ProcessorCount;
//        _processingTasks = new Task[processorCount];

//        for (int i = 0; i < processorCount; i++)
//        {
//            _processingTasks[i] = Task.Factory.StartNew(
//                () => ProcessLogQueueAsync(i),
//                _cancellationTokenSource.Token,
//                TaskCreationOptions.LongRunning,
//                TaskScheduler.Default);
//        }

//        // 定时刷新
//        _flushTimer = new Timer(
//            _ => FlushAsync().AsTask(),
//            null,
//            _config.FlushInterval,
//            _config.FlushInterval);
//    }

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    public async ValueTask LogAsync(string message, LogLevel level = LogLevel.Information, CancellationToken cancellationToken = default)
//    {
//        using var activity = _activitySource.StartActivity("LogAsync", ActivityKind.Internal);

//        var writer = new UnsafeBufferWriter();
//        try
//        {
//            LogFormatter.FormatLogEntry(writer, level, message);
//            var logData = writer.WrittenMemory;

//            var logEntry = _logEntryPool.Rent(logData);

//            // 尝试快速写入
//            if (_logChannel.Writer.TryWrite(logEntry))
//                return;

//            // 队列满，使用异步写入
//            await _logChannel.Writer.WriteAsync(logEntry, cancellationToken);
//        }
//        finally
//        {
//            writer.Dispose();
//        }
//    }

//    private async Task ProcessLogQueueAsync(int threadIndex)
//    {
//        // 设置线程亲和性
//        _threadAffinityManager.SetThreadAffinity(threadIndex);

//        var batchWriter = new UnsafeBufferWriter(_config.FileBufferSize);
//        var batchSize = 0;

//        try
//        {
//            await foreach (var logEntry in _logChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
//            {
//                try
//                {
//                    batchWriter.Write(logEntry.Data.Span);
//                    batchSize++;

//                    if (batchSize >= 1000 || batchWriter.WrittenMemory.Length >= _config.FileBufferSize)
//                    {
//                        await FlushBatchAsync(batchWriter);
//                        batchWriter.Reset();
//                        batchSize = 0;
//                    }
//                }
//                finally
//                {
//                    logEntry.Return();
//                }
//            }

//            // 处理剩余日志
//            if (batchSize > 0)
//            {
//                await FlushBatchAsync(batchWriter);
//            }
//        }
//        catch (OperationCanceledException)
//        {
//            // 正常关闭
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Critical error in log processor: {ex}");
//        }
//        finally
//        {
//            batchWriter.Dispose();
//        }
//    }

//    private async Task FlushBatchAsync(UnsafeBufferWriter batchWriter)
//    {
//        var sequence = new ReadOnlySequence<byte>(batchWriter.WrittenMemory);
//        await _platformIO.WriteAsync(sequence, _cancellationTokenSource.Token);
//    }

//    public async ValueTask FlushAsync()
//    {
//        await _platformIO.FlushAsync(_cancellationTokenSource.Token);
//    }

//    private IPlatformIO CreatePlatformIO(string logPath)
//    {
//        var systemType = SystemDetector.DetectSystemType();
//        var diskType = SystemDetector.DetectDiskType(logPath);

//        if (systemType == OperatingSystemType.Windows && _config.UseDirectIO)
//        {
//            return new WindowsDirectIO(logPath);
//        }
//        else if (systemType == OperatingSystemType.Linux && _config.UseIoUring && diskType == DiskType.NVMe)
//        {
//            return new LinuxIoUringWriter(logPath);
//        }
//        else
//        {
//            return new WindowsIO(logPath);
//        }
//    }

//    public async ValueTask DisposeAsync()
//    {
//        try
//        {
//            _cancellationTokenSource.Cancel();
//            _logChannel.Writer.Complete();
//            _flushTimer.Dispose();

//            await Task.WhenAll(_processingTasks);
//            await _platformIO.FlushAsync();
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error disposing logger: {ex}");
//        }
//        finally
//        {
//            _cancellationTokenSource.Dispose();
//            await _platformIO.DisposeAsync();
//        }
//    }
//}

//// ==========================
//// 性能测试工具
//// ==========================

//public class LoggerBenchmark
//{
//    public async Task RunBenchmark(int messageCount, int messageSize = 100)
//    {
//        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "benchmark.log");
//        if (File.Exists(logPath)) File.Delete(logPath);

//        using var logger = new SuperLogger(logPath);

//        Console.WriteLine($"Starting benchmark with {messageCount} messages of size {messageSize} bytes...");

//        var messages = Enumerable.Range(1, messageCount)
//            .Select(i => new string('X', messageSize))
//            .ToList();

//        var stopwatch = Stopwatch.StartNew();

//        // 预热
//        await logger.LogAsync("Warmup message");
//        await Task.Delay(100);

//        // 并行写入
//        await Task.WhenAll(messages.Select(msg => logger.LogAsync(msg)));

//        // 确保所有日志被写入
//        await logger.FlushAsync();

//        stopwatch.Stop();

//        var totalBytes = messageCount * messageSize;
//        var throughput = totalBytes / stopwatch.Elapsed.TotalSeconds / (1024 * 1024); // MB/s
//        var messagesPerSecond = messageCount / stopwatch.Elapsed.TotalSeconds;

//        Console.WriteLine($"Benchmark completed in {stopwatch.Elapsed.TotalSeconds:F2} seconds");
//        Console.WriteLine($"Throughput: {throughput:F2} MB/s");
//        Console.WriteLine($"Messages per second: {messagesPerSecond:F0}");
//        Console.WriteLine($"Average latency per message: {stopwatch.Elapsed.TotalMilliseconds / messageCount:F2} ms");

//        // 清理
//        if (File.Exists(logPath)) File.Delete(logPath);
//    }
//}

//// ==========================
//// 使用示例
//// ==========================

//public static class Program
//{
//    public static async Task Main()
//    {
//        // 使用示例
//        using var logger = new SuperLogger("application.log");

//        // 简单日志记录
//        await logger.LogAsync("Application started");

//        // 高性能批量记录
//        var sw = Stopwatch.StartNew();
//        var tasks = Enumerable.Range(1, 1_000_000)
//            .Select(i => logger.LogAsync($"High performance log message {i}"));

//        await Task.WhenAll(tasks);
//        sw.Stop();

//        Console.WriteLine($"Logged 1 million messages in {sw.ElapsedMilliseconds} ms");

//        // 性能测试
//        var benchmark = new LoggerBenchmark();
//        await benchmark.RunBenchmark(10_000_000);
//    }
//}