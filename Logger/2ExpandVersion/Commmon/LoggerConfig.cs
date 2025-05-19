using Server.Logger.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logger._2ExpandVersion.Commmon
{
    // 日志配置类
    public class LoggerConfig
    {
        public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Information;
        public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
        public string LogFilePath { get; set; } = "application.log";
        public bool EnableAsyncWriting { get; set; } = true;
        public int MaxQueueSize { get; set; } = 1_000_000;
        public int BatchSize { get; set; } = 10_000;
        public int FlushInterval { get; set; } = 500;
        public bool EnableConsoleColor { get; set; } = true;
        public int MaxRetryCount { get; set; } = 3;
        public int RetryDelayMs { get; set; } = 100;
    }
}
