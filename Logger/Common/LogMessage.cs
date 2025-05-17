//using Server.Common.Extensions;

//namespace Server.Logger.Common
//{
//    #region Log Message Structure
//        public class LogMessage
//        {
//            public DateTime Timestamp { get; set; }       // 日志时间戳
//            public LogLevel Level { get; set; }           // 日志级别
//            public string Message { get; set; }           // 日志内容
//            public int ThreadId { get; set; }             // 线程ID
//            public string ThreadName { get; set; }        // 线程名称
//            public Exception Exception { get; set; }      // 异常信息（可选）

//            // 构造函数
//            public LogMessage(
//                DateTime timestamp,
//                LogLevel level,
//                string message,
//                int threadId,
//                string threadName,
//                Exception exception = null)
//            {
//                Timestamp = timestamp;
//                Level = level;
//                Message = message;
//                ThreadId = threadId;
//                ThreadName = threadName;
//                Exception = exception;
//            }

//            // 可选：将日志级别转换为字符串（如"ERROR"/"INFO"）
//            public string LevelMessage => Level.ToString().ToUpperInvariant();
//        }
    
//    #endregion
//}
