using Server.Logger.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Logger._2ExpandVersion.Commmon
{
    // 日志接口
    public interface ILogger : IDisposable
    {
        LogLevel ConsoleLogLevel { get; set; }
        LogLevel FileLogLevel { get; set; }
        string LogFilePath { get; set; }
        bool EnableAsyncWriting { get; set; }

        void AddTemplate(LogTemplate template);
        void RemoveTemplate(string templateName);
        LogTemplate GetTemplate(string templateName);

        void Log(LogLevel level, string message, Exception exception = null,
            Dictionary<string, object> properties = null, string templateName = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0);

        void LogTrace(string message, Dictionary<string, object> properties = null, string templateName = null);
        void LogDebug(string message, Dictionary<string, object> properties = null, string templateName = null);
        void LogInformation(string message, Dictionary<string, object> properties = null, string templateName = null);

        void LogWarning(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);
        void LogError(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);
        void LogCritical(string message, Exception exception = null, Dictionary<string, object> properties = null, string templateName = null);

        void LogTrace<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogDebug<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogInformation<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogWarning<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogError<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogCritical<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
    }
}
