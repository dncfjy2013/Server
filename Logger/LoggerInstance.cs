using Microsoft.Win32.SafeHandles;
using Server.Logger.Output;
using System;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Server.Logger.Output
{
    public interface ILogOutput
    {
        void Write(LogMessage message);
        void Flush();
        void Close();
    }
}

namespace Server.Logger
{
    public class ConsoleOutput : ILogOutput
    {
        private readonly LoggerConfig _config;

        public ConsoleOutput(LoggerConfig config)
        {
            _config = config;
            Console.OutputEncoding = Encoding.UTF8;
        }

        public void Write(LogMessage message)
        {
            if (!_config.EnableConsoleWriting || message.Level < _config.ConsoleLogLevel)
                return;

            try
            {
                string text = LogMessageFormatter.Format(message);
                if (_config.EnableConsoleColor)
                {
                    var color = Console.ForegroundColor;
                    Console.ForegroundColor = GetColor(message.Level);
                    Console.Write(text);
                    Console.ForegroundColor = color;
                }
                else Console.Write(text);
            }
            catch { }
        }

        private ConsoleColor GetColor(LogLevel level) => level switch
        {
            LogLevel.Critical => ConsoleColor.Red,
            LogLevel.Error => ConsoleColor.DarkRed,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Information => ConsoleColor.Green,
            LogLevel.Debug => ConsoleColor.Cyan,
            _ => ConsoleColor.Gray
        };

        public void Flush() { }
        public void Close() { }
    }
}

namespace Server.Logger
{
    public class FileStreamOutput : ILogOutput, IDisposable
    {
        #region 字段
        private readonly LoggerConfig _config;
        private FileStream? _fileStream;
        private readonly byte[] _buffer;
        private int _bufferOffset;
        private readonly object _lock = new();
        private readonly Encoding _encoding = Encoding.UTF8;
        private string? _finalPath;
        private long _currentFileLength;
        private int _currentFileIndex;
        private string _currentDate;
        private bool _disposed;
        private readonly Regex _logFileRegex = new(@"_(\d+)\.dat$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        #endregion

        #region 构造函数
        public FileStreamOutput(LoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _buffer = new byte[config.File_Buffer_Size];
            _currentDate = DateTime.Now.ToString("yyyyMMdd");

            // 校验配置
            ValidateConfig();
            InitializeLastFileInfo();
        }
        #endregion

        #region 配置校验
        private void ValidateConfig()
        {
            if (string.IsNullOrEmpty(_config.LogDirectory))
                throw new ArgumentException("日志保存目录不能为空");

            if (_config.File_Split_Size <= 0)
                throw new ArgumentException("日志分割大小必须大于0");

            if (_config.File_Buffer_Size <= 0)
                throw new ArgumentException("缓冲区大小必须大于0");
        }
        #endregion

        #region 初始化历史文件信息
        private void InitializeLastFileInfo()
        {
            try
            {
                Directory.CreateDirectory(_config.LogDirectory);
                var searchPattern = $"Log_{_currentDate}_*.dat";
                var logFiles = Directory.GetFiles(_config.LogDirectory, searchPattern)
                                       .OrderBy(f => f)
                                       .ToList();

                if (logFiles.Count == 0)
                {
                    _currentFileIndex = 0;
                    return;
                }

                // 解析最后一个文件序号
                var lastFile = logFiles.Last();
                var match = _logFileRegex.Match(lastFile);
                if (!match.Success || !int.TryParse(match.Groups[1].Value, out var maxIndex))
                {
                    _currentFileIndex = 0;
                    return;
                }

                // 接续写入未写满的文件
                var fileInfo = new FileInfo(lastFile);
                if (fileInfo.Length > 0 && fileInfo.Length < _config.File_Split_Size)
                {
                    _currentFileIndex = maxIndex;
                    _finalPath = lastFile;
                    _currentFileLength = fileInfo.Length;
                    _fileStream = new FileStream(lastFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, _buffer.Length, true);
                    _fileStream.Seek(0, SeekOrigin.End);
                }
                else
                {
                    _currentFileIndex = maxIndex + 1;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[日志初始化失败] {ex}");
                _currentFileIndex = 0;
            }
        }
        #endregion

        #region 创建新日志文件
        private void CreateNewFile()
        {
            try
            {
                var now = DateTime.Now;
                var today = now.ToString("yyyyMMdd");

                // 跨天重置序号
                if (today != _currentDate)
                {
                    _currentDate = today;
                    _currentFileIndex = 0;
                }

                Directory.CreateDirectory(_config.LogDirectory);

                // 防止文件重名
                while (true)
                {
                    _finalPath = Path.Combine(_config.LogDirectory, string.Format(_config.LogFileNameFormat, now, _currentFileIndex));
                    if (!File.Exists(_finalPath)) break;
                    _currentFileIndex++;
                }

                // 创建文件流
                _fileStream = new FileStream(_finalPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, _buffer.Length, true);
                _currentFileLength = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[创建日志文件失败] {ex}");
                _fileStream = null;
            }
        }
        #endregion

        #region 写入日志（核心）
        public void Write(LogMessage message)
        {
            if (message.Level < _config.FileLogLevel || _disposed) return;

            lock (_lock)
            {
                try
                {
                    // 1. 检查跨天
                    if (CheckDateChange())
                    {
                        CloseCurrentFile();
                        CreateNewFile();
                    }

                    // 2. 格式化日志
                    var content = LogMessageFormatter.Format(message);
                    var data = _encoding.GetBytes(content);
                    if (data.Length == 0) return;

                    // 3. 无文件流则创建
                    _fileStream ??= CreateNewFileAndReturnStream();

                    // 4. 文件大小超限则切换
                    if (_currentFileLength >= _config.File_Split_Size)
                    {
                        CloseCurrentFile();
                        _fileStream = CreateNewFileAndReturnStream();
                    }

                    // 5. 写入缓冲
                    WriteToBuffer(data);
                    _currentFileLength += data.Length;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[日志写入失败] {ex}");
                }
            }
        }
        #endregion

        #region 缓冲写入逻辑
        private void WriteToBuffer(byte[] data)
        {
            int dataIndex = 0;
            int dataLength = data.Length;

            while (dataIndex < dataLength)
            {
                int freeSpace = _buffer.Length - _bufferOffset;
                if (freeSpace == 0) FlushBuffer();

                int copyLength = Math.Min(freeSpace, dataLength - dataIndex);
                Array.Copy(data, dataIndex, _buffer, _bufferOffset, copyLength);

                dataIndex += copyLength;
                _bufferOffset += copyLength;
            }

            // 达到阈值自动刷新
            if (_bufferOffset >= _config.Flush_Interval)
                FlushBuffer();
        }

        private void FlushBuffer()
        {
            if (_bufferOffset <= 0 || _fileStream == null) return;

            _fileStream.Write(_buffer, 0, _bufferOffset);
            _bufferOffset = 0;
        }
        #endregion

        #region 辅助方法
        /// <summary>检查日期是否变化</summary>
        private bool CheckDateChange()
        {
            return DateTime.Now.ToString("yyyyMMdd") != _currentDate;
        }

        /// <summary>关闭当前文件并清理空文件</summary>
        private void CloseCurrentFile()
        {
            try
            {
                if (_fileStream == null) return;

                FlushBuffer();
                _fileStream.Flush(true);
                _fileStream.Dispose();
                _fileStream = null;

                // 清理空文件
                DeleteEmptyFile(_finalPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[关闭日志文件失败] {ex}");
            }
        }

        /// <summary>删除空文件</summary>
        private void DeleteEmptyFile(string? filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                    File.Delete(filePath);
            }
            catch
            {
                // 忽略删除失败
            }
        }

        /// <summary>创建文件并返回流</summary>
        private FileStream? CreateNewFileAndReturnStream()
        {
            CreateNewFile();
            return _fileStream;
        }
        #endregion

        #region 公开方法
        public void Flush()
        {
            lock (_lock)
            {
                if (_disposed || _fileStream == null) return;
                try { FlushBuffer(); _fileStream.Flush(true); }
                catch (Exception ex) { Console.WriteLine($"[日志刷新失败] {ex}"); }
            }
        }

        public void Close() => Dispose();
        #endregion

        #region 资源释放
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                lock (_lock)
                {
                    try
                    {
                        FlushBuffer();
                        _fileStream?.Flush(true);
                        _fileStream?.Dispose();
                        DeleteEmptyFile(_finalPath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[释放日志资源失败] {ex}");
                    }
                }
            }

            _disposed = true;
            _fileStream = null;
        }

        ~FileStreamOutput() => Dispose(false);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}

namespace Server.Logger
{
    public class MemoryMappedOutput : ILogOutput, IDisposable
    {
        private readonly LoggerConfig _config;
        private readonly Encoding _encoding = Encoding.UTF8;
        private readonly object _lock = new object();
        private readonly string _dateFormat = "yyyyMMdd";
        private readonly int _processId;

        // 内存映射核心对象
        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private int _position;

        // 日志文件管理
        private string _currentDate;
        private int _currentFileIndex;
        private string _currentFilePath;
        private long _currentFileSize;
        private bool _disposed;

        public MemoryMappedOutput(LoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _processId = Environment.ProcessId; // 修复：进程ID赋值
            _currentDate = DateTime.Now.ToString(_dateFormat);

            // 创建日志目录
            if (!Directory.Exists(_config.LogDirectory))
                Directory.CreateDirectory(_config.LogDirectory);

            // 注册退出事件
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // 初始化日志文件
            _currentFileIndex = GetLastFileIndex() + 1;
            _currentFilePath = GetLogFilePath();
            _currentFileSize = GetFileLength(_currentFilePath);

            // 创建内存映射缓冲区
            CreateNewMappedFile();
            // 恢复崩溃未刷新日志
            RecoverUnflushedLogs();
        }

        #region 内存映射文件创建
        /// <summary>
        /// 创建新的内存映射文件
        /// </summary>
        private void CreateNewMappedFile()
        {
            lock (_lock)
            {
                try
                {
                    _accessor?.Dispose();
                    _mmf?.Dispose();

                    string mmfName = $"LogMMF_{_processId}_{Guid.NewGuid():N}";
                    _mmf = MemoryMappedFile.CreateNew(mmfName, _config.MMF_BUFFER_SIZE, MemoryMappedFileAccess.ReadWrite);
                    _accessor = _mmf.CreateViewAccessor();
                    _position = 0;
                }
                catch (Exception ex)
                {
                    _accessor = null;
                    _mmf = null;
                    WriteDirectToFile(_encoding.GetBytes($"[日志系统] MMF初始化失败，降级直接写文件：{ex.Message}\r\n"));
                }
            }
        }
        #endregion

        #region 日志写入核心
        /// <summary>
        /// 写入日志消息
        /// </summary>
        public void Write(LogMessage message)
        {
            // 级别过滤 + 资源释放检查
            if (message.Level < _config.FileLogLevel || _disposed)
                return;

            lock (_lock)
            {
                string content = string.Empty;
                try
                {
                    CheckDateChange();
                    content = LogMessageFormatter.Format(message);
                    byte[] data = _encoding.GetBytes(content);

                    if (_accessor == null)
                    {
                        WriteDirectToFile(data);
                        return;
                    }

                    if (_position + data.Length > _config.MMF_BUFFER_SIZE)
                        FlushToFile();
                    _accessor.WriteArray(_position, data, 0, data.Length);
                    _position += data.Length;

                    if (_position >= _config.MMF_FLUSH_THRESHOLD)
                        FlushToFile();
                }
                catch (Exception ex)
                {
                    WriteDirectToFile(_encoding.GetBytes($"[日志系统] 写入异常：{ex.Message}\r\n{content}\r\n"));
                }
            }
        }
        #endregion

        #region 刷新到磁盘
        /// <summary>
        /// 将内存缓冲区数据刷新到文件
        /// </summary>
        private void FlushToFile()
        {
            if (_position == 0 || _accessor == null)
                return;

            lock (_lock)
            {
                try
                {
                    byte[] buffer = new byte[_position];
                    _accessor.ReadArray(0, buffer, 0, _position);

                    // 文件大小超限，切换新文件
                    if (_currentFileSize + buffer.Length > _config.MMF_Split_Size)
                        SwitchNewFile();

                    // 高性能写入：异步、顺序扫描、共享读
                    using var fs = new FileStream(
                        _currentFilePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.Read,
                        4096,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    fs.Write(buffer, 0, buffer.Length);
                    _currentFileSize += buffer.Length;
                    _position = 0; // 重置缓冲区
                }
                catch (Exception ex)
                {
                    WriteDirectToFile(_encoding.GetBytes($"[日志系统] 刷新异常：{ex.Message}\r\n"));
                }
            }
        }

        /// <summary>
        /// 直接写入文件（降级方案）
        /// </summary>
        private void WriteDirectToFile(byte[] data)
        {
            lock (_lock)
            {
                try
                {
                    if (_currentFileSize + data.Length > _config.File_Split_Size)
                        SwitchNewFile();

                    using var fs = new FileStream(_currentFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096);
                    fs.Write(data, 0, data.Length);
                    _currentFileSize += data.Length;
                }
                catch { /* 忽略写入失败，保证程序运行 */ }
            }
        }
        #endregion

        #region 文件切分管理
        /// <summary>
        /// 检查日期变更，自动切换日志文件
        /// </summary>
        private void CheckDateChange()
        {
            string today = DateTime.Now.ToString(_dateFormat);
            if (today == _currentDate)
                return;

            // 日期变更：刷新旧日志，重置文件序号
            FlushToFile();
            _currentDate = today;
            _currentFileIndex = 0;
            _currentFilePath = GetLogFilePath();
            _currentFileSize = 0;
        }

        /// <summary>
        /// 切换到新日志文件
        /// </summary>
        private void SwitchNewFile()
        {
            _currentFileIndex++;
            _currentFilePath = GetLogFilePath();
            _currentFileSize = 0; // 修复：重置文件大小
        }

        /// <summary>
        /// 获取最后一个日志文件序号
        /// </summary>
        private int GetLastFileIndex()
        {
            try
            {
                var files = Directory.GetFiles(_config.LogDirectory, $"Log_{_currentDate}_*.dat");
                int max = -1;
                foreach (var f in files)
                {
                    var match = Regex.Match(f, @"_(\d+)\.dat$", RegexOptions.RightToLeft);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int idx))
                        max = Math.Max(max, idx);
                }
                return max;
            }
            catch { return -1; }
        }

        /// <summary>
        /// 获取日志文件完整路径
        /// </summary>
        private string GetLogFilePath() =>
            Path.Combine(_config.LogDirectory, $"Log_{_currentDate}_{_currentFileIndex:D4}.dat");

        /// <summary>
        /// 获取文件大小（初始化用）
        /// </summary>
        private long GetFileLength(string path)
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        #endregion

        #region 崩溃恢复（三重防丢）
        /// <summary>
        /// 启动时恢复未刷新日志
        /// </summary>
        private void RecoverUnflushedLogs()
        {
            lock (_lock)
            {
                string cachePath = Path.Combine(_config.LogDirectory, _config.CACHE_FILE_NAME);
                if (!File.Exists(cachePath))
                    return;

                try
                {
                    byte[] data = File.ReadAllBytes(cachePath);
                    if (data.Length == 0)
                    {
                        File.Delete(cachePath);
                        return;
                    }

                    // 恢复缓存日志
                    WriteDirectToFile(data);
                    File.Delete(cachePath);
                }
                catch { }
            }
        }

        /// <summary>
        /// 退出时缓存未刷新日志
        /// </summary>
        private void CacheUnflushedLogsOnExit()
        {
            lock (_lock)
            {
                if (_position == 0 || _accessor == null)
                    return;

                try
                {
                    // 多进程隔离缓存文件，防止覆盖
                    string cachePath = Path.Combine(_config.LogDirectory, $"Cache_{_processId}.tmp");
                    byte[] buffer = new byte[_position];
                    _accessor.ReadArray(0, buffer, 0, _position);
                    File.WriteAllBytes(cachePath, buffer);
                }
                catch { }
            }
        }

        // 退出事件
        private void OnProcessExit(object sender, EventArgs e) => SafeFlushAndCache();
        private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            SafeFlushAndCache();
            Environment.Exit(0);
        }
        // 未处理异常
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            SafeFlushAndCache();
        }

        /// <summary>
        /// 安全刷新+缓存
        /// </summary>
        private void SafeFlushAndCache()
        {
            FlushToFile();
            CacheUnflushedLogsOnExit();
        }
        #endregion

        #region 资源释放
        public void Flush()
        {
            lock (_lock)
            {
                if (!_disposed)
                    FlushToFile();
            }
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;

                try
                {
                    if (disposing)
                    {
                        // 释放托管资源
                        FlushToFile();
                        _accessor?.Dispose();
                        _mmf?.Dispose();
                    }
                    else
                    {
                        // 析构函数：强杀时缓存日志
                        try
                        {
                            if (_position > 0 && _accessor != null)
                            {
                                string cachePath = Path.Combine(_config.LogDirectory, $"Cache_{_processId}.tmp");
                                byte[] buffer = new byte[_position];
                                _accessor.ReadArray(0, buffer, 0, _position);
                                File.WriteAllBytes(cachePath, buffer);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        ~MemoryMappedOutput()
        {
            Dispose(false);
        }
        #endregion
    }
}

namespace Server.Logger
{
    public sealed class LoggerInstance : ILogger
    {
        #region 单例
        private static readonly ConcurrentDictionary<string, Lazy<LoggerInstance>> _instances = new();
        private const string Default = "Default";
        public static LoggerInstance Instance => GetInstance(Default);

        public static LoggerInstance GetInstance(string name, LoggerConfig config = null)
        {
            name = string.IsNullOrEmpty(name) ? Default : name;
            return _instances.GetOrAdd(name, k => new Lazy<LoggerInstance>(() => new LoggerInstance(config ?? new LoggerConfig { LogName = k }))).Value;
        }
        #endregion

        private readonly LoggerConfig _config;
        private readonly ConcurrentDictionary<string, LogTemplate> _templates = new();
        private readonly Channel<LogMessage> _channel;
        private readonly List<ILogOutput> _outputs = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task[] _workers;
        private bool _disposed;

        public LoggerInstance(LoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _channel = Channel.CreateBounded<LogMessage>(new BoundedChannelOptions(config.MaxQueueSize) { FullMode = BoundedChannelFullMode.DropOldest });

            _outputs.Add(new ConsoleOutput(config));
            if (config.UseMemoryMappedFile) _outputs.Add(new MemoryMappedOutput(config));
            else _outputs.Add(new FileStreamOutput(config));

            AddTemplate(new LogTemplate { Name = "Default", Level = LogLevel.Information, IncludeException = true });

            if (config.EnableAsyncWriting)
            {
                _workers = Enumerable.Range(0, config.MaxDegreeOfParallelism).Select(_ => Task.Factory.StartNew(ConsumeAsync, TaskCreationOptions.LongRunning)).ToArray();
            }
        }

        #region 日志核心
        public void Log<T>(LogLevel level, T state, Func<T, Exception, string> formatter = null, Exception ex = null, string template = null)
        {
            if (_disposed) return;
            var tpl = GetTemplate(template);
            if (level < tpl.Level) return;

            string msgText = string.Empty;
            if (formatter != null)
            {
                msgText = formatter(state, ex);
            }
            else if (state is byte[] bytes)
            {
                msgText = Encoding.UTF8.GetString(bytes);
            }
            else if (state is ReadOnlyMemory<byte> rom)
            {
                msgText = Encoding.UTF8.GetString(rom.Span);
            }
            else
            {
                msgText = state?.ToString() ?? "";
            }

            var logBytes = Encoding.UTF8.GetBytes(msgText);
            var log = new LogMessage(
                DateTime.Now,
                level,
                logBytes,  
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.Name,
                ex
            );

            if (_config.EnableAsyncWriting)
                _channel.Writer.TryWrite(log);
            else
                WriteToOutputs(log);
        }

        private async Task ConsumeAsync()
        {
            await foreach (var msg in _channel.Reader.ReadAllAsync(_cts.Token))
                WriteToOutputs(msg);
        }

        private void WriteToOutputs(LogMessage msg)
        {
            foreach (var o in _outputs) o.Write(msg);
        }
        #endregion

        #region 模板
        public void AddTemplate(LogTemplate t) => _templates[t.Name] = t;
        public void RemoveTemplate(string n) => _templates.TryRemove(n, out _);
        public LogTemplate GetTemplate(string n) => _templates.TryGetValue(n ?? "Default", out var t) ? t : _templates["Default"];
        #endregion

        #region ILogger
        public void LogTrace<T>(T s, Func<T, Exception, string> f = null, string t = null) => Log(LogLevel.Trace, s, f, null, t);
        public void LogDebug<T>(T s, Func<T, Exception, string> f = null, string t = null) => Log(LogLevel.Debug, s, f, null, t);
        public void LogInformation<T>(T s, Func<T, Exception, string> f = null, string t = null) => Log(LogLevel.Information, s, f, null, t);
        public void LogWarning<T>(T s, Exception e = null, Func<T, Exception, string> f = null, string t = null) => Log(LogLevel.Warning, s, f, e, t);
        public void LogError<T>(T s, Exception e = null, Func<T, Exception, string> f = null, string t = null) => Log(LogLevel.Error, s, f, e, t);
        public void LogCritical<T>(T s, Exception e = null, Func<T, Exception, string> f = null, string t = null) => Log(LogLevel.Critical, s, f, e, t);
        #endregion

        #region 释放
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            Task.WaitAll(_workers, 10000);
            while (_channel.Reader.TryRead(out var m)) WriteToOutputs(m);
            foreach (var o in _outputs) { o.Flush(); o.Close(); }
            _cts.Dispose();
        }
        #endregion
    }
}

namespace Server.Logger
{
    public static class LogMessageFormatter
    {
        private static readonly Encoding _encoding = Encoding.UTF8;

        public static string Format(LogMessage msg)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append($"{msg.Timestamp:yyyy-MM-dd HH:mm:ss.fff} ");
            sb.Append($"[{msg.Level.ToString().ToUpper()}] ");
            sb.Append($"[{msg.ThreadId}] ");

            string message = _encoding.GetString(msg.Message.Span);
            sb.Append(message);

            if (msg.Exception != null)
            {
                sb.AppendLine();
                sb.Append("【异常】");
                sb.Append(msg.Exception.ToString());
            }

            sb.AppendLine();
            return sb.ToString();
        }
    }
}