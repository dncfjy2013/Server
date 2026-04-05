namespace Logger
{
    public interface ILogger : IDisposable
    {
        void AddTemplate(LogTemplate template);
        void RemoveTemplate(string templateName);
        LogTemplate GetTemplate(string templateName);

        // 泛型快捷方法
        void Trace<T>(T state, Func<T, Exception, string>? formatter = null, string? templateName = null);
        void Debug<T>(T state, Func<T, Exception, string>? formatter = null, string? templateName = null);
        void Info<T>(T state, Func<T, Exception, string>? formatter = null, string? templateName = null);
        void Warn<T>(T state, Exception? exception = null, Func<T, Exception, string>? formatter = null, string? templateName = null);
        void Error<T>(T state, Exception? exception = null, Func<T, Exception, string>? formatter = null, string? templateName = null);
        void Critical<T>(T state, Exception? exception = null, Func<T, Exception, string>? formatter = null, string? templateName = null);
    }

    public interface ILogOutput
    {
        void Write(LogMessage message);
        void Flush();
        void Close();
    }
}