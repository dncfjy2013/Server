using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Logger.Core
{
    public class OutputFileStream : ILogOutput, IDisposable
    {
        private readonly LoggerConfig _config;
        private FileStream? _fileStream = null;
        private readonly byte[] _buffer;
        private int _bufferOffset;
        private readonly object _lock = new object();
        private readonly Encoding _encoding = Encoding.UTF8;
        private string? _finalPath;
        private long _currentFileLength;
        private int _currentFileIndex;
        private string _currentDate;
        private bool _disposed;
        private static readonly Regex _logFileRegex = new(@"_(\d+)\.dat$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public OutputFileStream(LoggerConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _buffer = new byte[config.File_Buffer_Size];
            _currentDate = DateTime.Now.ToString("yyyyMMdd");
            ValidateConfig();
            InitializeLastFileInfo();
        }

        private void ValidateConfig()
        {
            if (string.IsNullOrEmpty(_config.LogDirectory))
                throw new ArgumentException("日志保存目录不能为空");
            if (_config.File_Split_Size <= 0)
                throw new ArgumentException("日志分割大小必须大于0");
            if (_config.File_Buffer_Size <= 0)
                throw new ArgumentException("缓冲区大小必须大于0");
        }

        private void InitializeLastFileInfo()
        {
            try
            {
                Directory.CreateDirectory(_config.LogDirectory);
                var files = Directory.GetFiles(_config.LogDirectory, $"Log_{_currentDate}_*.dat");
                int maxIndex = -1;
                string lastFile = string.Empty;

                foreach (var f in files)
                {
                    var match = _logFileRegex.Match(f);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var idx))
                    {
                        if (idx > maxIndex)
                        {
                            maxIndex = idx;
                            lastFile = f;
                        }
                    }
                }

                if (maxIndex == -1)
                {
                    _currentFileIndex = 0;
                    return;
                }

                var info = new FileInfo(lastFile);
                if (info.Length > 0 && info.Length < _config.File_Split_Size)
                {
                    _currentFileIndex = maxIndex;
                    _finalPath = lastFile;
                    _currentFileLength = info.Length;
                    _fileStream = new FileStream(lastFile, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, _buffer.Length, true);
                    _fileStream.Seek(0, SeekOrigin.End);
                }
                else
                {
                    _currentFileIndex = maxIndex + 1;
                }
            }
            catch
            {
                _currentFileIndex = 0;
            }
        }

        private void CreateNewFile()
        {
            try
            {
                var today = DateTime.Now.ToString("yyyyMMdd");
                if (today != _currentDate)
                {
                    _currentDate = today;
                    _currentFileIndex = 0;
                }

                Directory.CreateDirectory(_config.LogDirectory);
                while (true)
                {
                    _finalPath = Path.Combine(_config.LogDirectory, string.Format(_config.LogFileNameFormat, DateTime.Now, _currentFileIndex));
                    if (!File.Exists(_finalPath)) break;
                    _currentFileIndex++;
                }

                _fileStream = new FileStream(_finalPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, _buffer.Length, true);
                _currentFileLength = 0;
            }
            catch
            {
                _fileStream = null;
            }
        }

        public void Write(LogMessage message)
        {
            if (message.Level < _config.FileLogLevel || _disposed) return;

            lock (_lock)
            {
                try
                {
                    if (DateTime.Now.ToString("yyyyMMdd") != _currentDate)
                    {
                        CloseCurrentFile();
                        CreateNewFile();
                    }

                    var data = _encoding.GetBytes(LogMessageFormatter.Format(message));
                    if (data.Length == 0) return;

                    _fileStream ??= CreateNewFileAndReturnStream();
                    if (_currentFileLength >= _config.File_Split_Size)
                    {
                        CloseCurrentFile();
                        _fileStream = CreateNewFileAndReturnStream();
                    }

                    WriteToBuffer(data);
                    _currentFileLength += data.Length;
                }
                catch { }
            }
        }

        private void WriteToBuffer(byte[] data)
        {
            int dataIdx = 0;
            int len = data.Length;
            while (dataIdx < len)
            {
                int free = _buffer.Length - _bufferOffset;
                if (free == 0) FlushBuffer();

                int copy = Math.Min(free, len - dataIdx);
                Array.Copy(data, dataIdx, _buffer, _bufferOffset, copy);

                dataIdx += copy;
                _bufferOffset += copy;
            }

            if (_bufferOffset >= _config.Flush_Interval)
                FlushBuffer();
        }

        private void FlushBuffer()
        {
            if (_bufferOffset <= 0 || _fileStream == null) return;
            _fileStream.Write(_buffer, 0, _bufferOffset);
            _bufferOffset = 0;
        }

        private void CloseCurrentFile()
        {
            try
            {
                if (_fileStream == null) return;
                FlushBuffer();
                _fileStream.Flush(true);
                _fileStream.Dispose();
                _fileStream = null;

                if (new FileInfo(_finalPath).Length == 0)
                    File.Delete(_finalPath);
            }
            catch { }
        }

        private FileStream CreateNewFileAndReturnStream()
        {
            CreateNewFile();
            return _fileStream;
        }

        public void Flush()
        {
            lock (_lock)
            {
                if (_disposed || _fileStream == null) return;
                try { FlushBuffer(); _fileStream.Flush(true); }
                catch { }
            }
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                try
                {
                    FlushBuffer();
                    _fileStream?.Flush(true);
                    _fileStream?.Dispose();
                    if (new FileInfo(_finalPath).Length == 0)
                        File.Delete(_finalPath);
                }
                catch { }
            }
        }
    }
}
