namespace Logger
{
    public sealed class LoggerConfig
    {
        public string LogName = "DefaultName";
        public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Trace;
        public LogLevel FileLogLevel { get; set; } = LogLevel.Trace;
        public string LogDirectory { get; set; } = "Logs";
        public string LogFileNameFormat { get; set; } = "Log_{0:yyyyMMdd}_{1:D3}.dat";
        public bool EnableAsyncWriting { get; set; } = true;
        public int MaxQueueSize { get; set; } = int.MaxValue;
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
        public LogOutputType UseMemoryMappedType { get; set; } = LogOutputType.Console;


        public bool EnableConsoleColor { get; set; } = true;


        public int Flush_Interval { get; set; } = 500;
        public int File_Buffer_Size { get; set; } = 64 * 1024;
        public long File_Split_Size { get; set; } = 100L * 1024 * 1024;


        public long MMF_BUFFER_SIZE { get; set; } = 5 * 1024 * 1024; // 100MB
        public long MMF_FLUSH_THRESHOLD { get; set; } = 1 * 1024 * 1024;  // 16MB自动刷盘
        public long MMF_Split_Size { get; set; } = 64 * 1024;
        public string CACHE_FILE_NAME { get; set; } = "mmf_cache.tmp";
    }
}
