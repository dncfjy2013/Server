// 日志消息类 - 使用struct减少GC压力
using Server.Logger.Common;

public readonly struct LogMessage
{
    public readonly DateTime Timestamp;
    public readonly LogLevel Level;
    public readonly ReadOnlyMemory<byte> Message;
    public readonly int ThreadId;
    public readonly string ThreadName;
    public readonly Exception Exception;
    public readonly IReadOnlyDictionary<string, object> Properties;

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
    }
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