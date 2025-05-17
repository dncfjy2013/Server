//using Server.Logger.Common;
//using System.Runtime.CompilerServices;

//namespace Server.Logger
//{
//    // 日志模板配置
//    public class LogTemplate
//    {
//        public string Name { get; set; }
//        public string Template { get; set; }
//        public LogLevel Level { get; set; }
//        public bool IncludeException { get; set; }
//        public bool IncludeCallerInfo { get; set; }
//    }

//    public interface ILogger : IDisposable
//    {
//        LogLevel ConsoleLogLevel { get; set; }
//        LogLevel FileLogLevel { get; set; }
//        string LogFilePath { get; set; }
//        bool EnableAsyncWriting { get; set; }

//        // 模板管理
//        void AddTemplate(LogTemplate template);
//        void RemoveTemplate(string templateName);
//        LogTemplate GetTemplate(string templateName);

//        // 基础日志方法
//        void Log(LogLevel level, string message, Exception exception = null,
//            Dictionary<string, object> properties = null, string templateName = null,
//            [CallerMemberName] string memberName = "",
//            [CallerFilePath] string sourceFilePath = "",
//            [CallerLineNumber] int sourceLineNumber = 0);

//        // 快捷日志方法 - 无异常
//        void LogTrace(string message, Dictionary<string, object> properties = null, string templateName = null);
//        void LogDebug(string message, Dictionary<string, object> properties = null, string templateName = null);
//        void LogInformation(string message, Dictionary<string, object> properties = null, string templateName = null);

//        // 快捷日志方法 - 带异常
//        void LogWarning(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);
//        void LogError(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);
//        void LogCritical(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);

//        // 结构化日志方法
//        void LogTrace<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
//        void LogDebug<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
//        void LogInformation<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
//        void LogWarning<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
//        void LogError<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
//        void LogCritical<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
//    }
//}