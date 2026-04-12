using Logger;
using Logger.Core;
using System.Text;
using System.Text.RegularExpressions;

public sealed class OutputFastSafe : ILogOutput
{
    #region 配置
    private readonly LoggerConfig _config;
    private readonly Encoding _encoding = Encoding.UTF8;
    private const string DateFormat = "yyyyMMdd";
    #endregion

    #region 写入核心
    private readonly byte[] _buffer;
    private readonly object _lock = new();
    private int _bufferPos;

    private FileStream? _stream;
    private string _currentDate;
    private int _fileIndex;
    private string _filePath;
    private long _fileSize; 
    private bool _disposed;
    #endregion

    public OutputFastSafe(LoggerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _buffer = new byte[1024 * 128]; 
        _currentDate = DateTime.Now.ToString(DateFormat);

        Directory.CreateDirectory(_config.LogDirectory);
        _fileIndex = GetLastFileIndex() + 1;
        _filePath = BuildFilePath();
        _fileSize = GetFileSize(_filePath);

        CreateFileStream();
    }

    public void Write(LogMessage message)
    {
        if (_disposed || message.Level < _config.FileLogLevel) return;

        try
        {
            string content = LogMessageFormatter.Format(message);
            int bytes = _encoding.GetByteCount(content);
            if (bytes <= 0 || bytes > _buffer.Length) return;

            lock (_lock)
            {
                if (_fileSize + bytes > _config.File_Split_Size)
                {
                    FlushBuffer();
                    SplitNewFile();
                }

                if (_bufferPos + bytes > _buffer.Length)
                    FlushBuffer();

                _encoding.GetBytes(content, 0, content.Length, _buffer, _bufferPos);
                _bufferPos += bytes;

                if (_bufferPos >= _buffer.Length - 4096)
                    FlushBuffer();
            }
        }
        catch
        {

        }
    }

    #region 底层刷新
    private void FlushBuffer()
    {
        if (_bufferPos == 0 || _disposed || _stream == null) return;
        try
        {
            CheckDateRoll();

            _stream.Write(_buffer, 0, _bufferPos);
            _stream.Flush(flushToDisk: false);

            _fileSize += _bufferPos;
            _bufferPos = 0;
        }
        catch
        {
            _bufferPos = 0;
        }
    }
    #endregion

    #region 文件管理
    private void CreateFileStream()
    {
        try
        {
            _stream?.Dispose();
            _stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
        }
        catch
        {
            _stream = null;
        }
    }

    private void CheckDateRoll()
    {
        string today = DateTime.Now.ToString(DateFormat);
        if (today == _currentDate) return;

        _currentDate = today;
        _fileIndex = 0;
        _filePath = BuildFilePath();
        _fileSize = 0;
        CreateFileStream();
    }

    private void SplitNewFile()
    {
        _fileIndex++;
        _filePath = BuildFilePath();
        _fileSize = 0;
        CreateFileStream();
    }

    private int GetLastFileIndex()
    {
        try
        {
            var files = Directory.GetFiles(_config.LogDirectory, $"Log_{_currentDate}_*.dat");
            int max = -1;
            foreach (var f in files)
            {
                var m = Regex.Match(f, @"_(\d+)\.dat$", RegexOptions.RightToLeft);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int idx))
                    max = Math.Max(max, idx);
            }
            return max;
        }
        catch { return -1; }
    }

    private string BuildFilePath() => Path.Combine(_config.LogDirectory, $"Log_{_currentDate}_{_fileIndex:D4}.dat");
    private long GetFileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
    #endregion

    #region 释放
    public void Flush()
    {
        lock (_lock)
        {
            FlushBuffer();
            _stream?.Flush(flushToDisk: true);
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            FlushBuffer();
            _stream?.Dispose();
        }
        GC.SuppressFinalize(this);
    }
    #endregion
}