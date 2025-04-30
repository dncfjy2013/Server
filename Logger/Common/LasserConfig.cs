using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Logger.Common
{
    #region Configuration
    public class LoggerConfig
    {
        public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Trace;
        public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
        public string LogFilePath { get; set; } = "application.log";
        public bool EnableAsyncWriting { get; set; } = true;
    }
    #endregion
}
