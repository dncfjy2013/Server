namespace Logger
{
    public readonly struct LogMessage
    {
        public readonly DateTime Timestamp;
        public readonly LogLevel Level;
        public string Message { get; }
        public readonly int ThreadId;
        public readonly string ThreadName;
        public readonly Exception Exception;
        public LogMessage(DateTime timestamp, LogLevel level, string message, int threadId, string threadName,
            Exception exception = null)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
            ThreadId = threadId;
            ThreadName = threadName;
            Exception = exception;
        }
    }

    // 日志模板配置
    public sealed class LogTemplate
    {
        public string Name { get; set; }
        public LogLevel Level { get; set; }
        public bool IncludeException { get; set; }
    }
}