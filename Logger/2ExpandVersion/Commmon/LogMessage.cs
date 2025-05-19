using Server.Logger.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logger._2ExpandVersion.Commmon
{
    // 日志消息类
    public class LogMessage
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }
        public int ThreadId { get; set; }
        public string ThreadName { get; set; }
        public Exception Exception { get; set; }
        public Dictionary<string, object> Properties { get; set; }

        public LogMessage() { }

        public LogMessage(DateTime timestamp, LogLevel level, string message, int threadId, string threadName,
            Exception exception = null, Dictionary<string, object> properties = null)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
            ThreadId = threadId;
            ThreadName = threadName;
            Exception = exception;
            Properties = properties;
        }

        public void Reset()
        {
            Timestamp = DateTime.MinValue;
            Level = LogLevel.Information;
            Message = null;
            ThreadId = 0;
            ThreadName = null;
            Exception = null;

            if (Properties != null)
            {
                Properties.Clear();
            }
            else
            {
                Properties = new Dictionary<string, object>();
            }
        }

        public string LevelMessage => Level.ToString().ToUpperInvariant().Center(11, " ");
    }
}
