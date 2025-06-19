// 日志接口
using Server.Logger.Common;
using System.Runtime.CompilerServices;

namespace Server.Logger
{
    public interface ILogger : IDisposable
    {
        void AddTemplate(LogTemplate template);
        void RemoveTemplate(string templateName);
        LogTemplate GetTemplate(string templateName);

        void Log(LogLevel level, ReadOnlyMemory<byte> message, Exception exception = null,
            IReadOnlyDictionary<string, object> properties = null, string templateName = null,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string sourceFilePath = "",
            [CallerLineNumber] int sourceLineNumber = 0);

        // 泛型日志方法
        void Log<T>(LogLevel level, T state, Func<T, Exception, string> formatter = null,
            Exception exception = null, IReadOnlyDictionary<string, object> properties = null,
            string templateName = null);

        // 快捷日志方法
        void LogTrace(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogDebug(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogInformation(ReadOnlyMemory<byte> message, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogWarning(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogError(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null);
        void LogCritical(ReadOnlyMemory<byte> message, Exception exception = null, IReadOnlyDictionary<string, object> properties = null, string templateName = null);

        // 泛型快捷方法
        void LogTrace<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogDebug<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogInformation<T>(T state, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogWarning<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogError<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
        void LogCritical<T>(T state, Exception exception = null, Func<T, Exception, string> formatter = null, string templateName = null);
    }
}