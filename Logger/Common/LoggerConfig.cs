// 日志配置类
using Server.Logger.Common;

public sealed class LoggerConfig
{
    public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Trace;
    public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
    public string LogDirectory { get; set; } = "Logs";
    public string LogFileNameFormat => "Log_{0:yyyyMMdd}_{1:D3}.dat";
    public bool EnableAsyncWriting { get; set; } = true;
    public bool EnableConsoleWriting { get; set; } = false;
    public int MaxQueueSize { get; set; } = int.MaxValue;
    public int FlushInterval { get; set; } = 500;
    public bool EnableConsoleColor { get; set; } = true;
    public int FileBufferSize { get; set; } = 64 * 1024;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public bool UseMemoryMappedFile { get; set; } = true;
    public long MemoryMappedFileSize { get; set; } = 1024 * 1024 * 1000; // 1000MB
    public long MemoryMappedThreadShould { get; set; } = 100 * 1024 * 1024;  // 100MB
}
