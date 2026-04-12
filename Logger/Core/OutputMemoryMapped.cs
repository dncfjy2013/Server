using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Logger.Core
{
    public class OutputMemoryMapped : ILogOutput, IDisposable
    {
        private readonly LoggerConfig _config;
        private readonly Encoding _encoding = Encoding.UTF8;
        private readonly object _lock = new object();
        private readonly string _dateFormat = "yyyyMMdd";
        private readonly int _processId;

        // 内存映射核心对象
        private MemoryMappedFile? _mmf = null;
        private MemoryMappedViewAccessor? _accessor = null;
        private int _position;

        // 日志文件管理
        private string _currentDate;
        private int _currentFileIndex;
        private string _currentFilePath;
        private long _currentFileSize;
        private bool _disposed;

        // 高性能优化：复用缓冲区，避免反复分配
        private byte[] _sharedBuffer;

        public OutputMemoryMapped(LoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _processId = Environment.ProcessId;
            _currentDate = DateTime.Now.ToString(_dateFormat);
            _sharedBuffer = new byte[_config.MMF_BUFFER_SIZE]; // 预分配

            if (!Directory.Exists(_config.LogDirectory))
                Directory.CreateDirectory(_config.LogDirectory);

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            _currentFileIndex = GetLastFileIndex() + 1;
            _currentFilePath = GetLogFilePath();
            _currentFileSize = GetFileLength(_currentFilePath);

            CreateNewMappedFile();
            RecoverUnflushedLogs();
        }

        #region 内存映射文件创建
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
        public void Write(LogMessage message)
        {
            if (message.Equals(default(LogMessage)) || message.Level < _config.FileLogLevel || _disposed)
                return;

            lock (_lock)
            {
                try
                {
                    CheckDateChange();
                    string content = LogMessageFormatter.Format(message);
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
                    WriteDirectToFile(_encoding.GetBytes($"[日志系统] 写入异常：{ex.Message}\r\n"));
                }
            }
        }
        #endregion

        #region 刷新到磁盘（性能核心优化）
        private void FlushToFile()
        {
            if (_position == 0 || _accessor == null)
                return;

            try
            {
                // 高性能：复用缓冲区，不分配新数组
                if (_sharedBuffer.Length < _position)
                    _sharedBuffer = new byte[_config.MMF_BUFFER_SIZE];

                _accessor.ReadArray(0, _sharedBuffer, 0, _position);

                if (_currentFileSize + _position > _config.MMF_Split_Size)
                    SwitchNewFile();

                using var fs = new FileStream(
                    _currentFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);

                fs.Write(_sharedBuffer, 0, _position);
                _currentFileSize += _position;
                _position = 0;
            }
            catch (Exception ex)
            {
                WriteDirectToFile(_encoding.GetBytes($"[日志系统] 刷新异常：{ex.Message}\r\n"));
            }
        }
        #endregion

        #region 直接写入文件（降级）
        private void WriteDirectToFile(byte[] data)
        {
            try
            {
                if (_currentFileSize + data.Length > _config.File_Split_Size)
                    SwitchNewFile();

                using var fs = new FileStream(
                    _currentFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    4096,
                    FileOptions.SequentialScan);

                fs.Write(data, 0, data.Length);
                _currentFileSize += data.Length;
            }
            catch { }
        }
        #endregion

        #region 文件切分管理
        private void CheckDateChange()
        {
            string today = DateTime.Now.ToString(_dateFormat);
            if (today == _currentDate)
                return;

            FlushToFile();
            _currentDate = today;
            _currentFileIndex = 0;
            _currentFilePath = GetLogFilePath();
            _currentFileSize = 0;
        }

        private void SwitchNewFile()
        {
            _currentFileIndex++;
            _currentFilePath = GetLogFilePath();
            _currentFileSize = 0;
        }

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

        private string GetLogFilePath() =>
            Path.Combine(_config.LogDirectory, $"Log_{_currentDate}_{_currentFileIndex:D4}.dat");

        private long GetFileLength(string path)
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        #endregion

        #region 崩溃恢复
        private void RecoverUnflushedLogs()
        {
            lock (_lock)
            {
                string cachePath = Path.Combine(_config.LogDirectory, $"Cache_{_processId}.tmp");
                if (!File.Exists(cachePath)) return;

                try
                {
                    byte[] data = File.ReadAllBytes(cachePath);
                    if (data.Length > 0) WriteDirectToFile(data);
                    File.Delete(cachePath);
                }
                catch { }
            }
        }

        private void CacheUnflushedLogsOnExit()
        {
            if (_position == 0 || _accessor == null) return;

            try
            {
                string cachePath = Path.Combine(_config.LogDirectory, $"Cache_{_processId}.tmp");
                byte[] buffer = new byte[_position];
                _accessor.ReadArray(0, buffer, 0, _position);
                File.WriteAllBytes(cachePath, buffer);
            }
            catch { }
        }

        private void OnProcessExit(object? sender, EventArgs? e) => SafeFlushAndCache();
        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs? e)
        {
            e.Cancel = true;
            SafeFlushAndCache();
            Environment.Exit(0);
        }

        private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs? e)
        {
            SafeFlushAndCache();
        }

        private void SafeFlushAndCache()
        {
            lock (_lock)
            {
                if (_disposed) return;
                FlushToFile();
                CacheUnflushedLogsOnExit();
            }
        }
        #endregion

        #region 资源释放
        public void Flush()
        {
            lock (_lock)
            {
                if (!_disposed) FlushToFile();
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
                if (_disposed) return;
                _disposed = true;

                try
                {
                    if (disposing)
                    {
                        FlushToFile();
                        _accessor?.Dispose();
                        _mmf?.Dispose();
                    }
                    else
                    {
                        if (_position > 0 && _accessor != null)
                        {
                            try
                            {
                                string cachePath = Path.Combine(_config.LogDirectory, $"Cache_{_processId}.tmp");
                                byte[] buffer = new byte[_position];
                                _accessor.ReadArray(0, buffer, 0, _position);
                                File.WriteAllBytes(cachePath, buffer);
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }

        ~OutputMemoryMapped()
        {
            Dispose(false);
        }
        #endregion
    }
}
