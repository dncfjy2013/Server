using Logger;
using Logger.Core;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Logger
{
    public sealed class LoggerInstance : ILogger
    {
        #region 单例
        private static readonly ConcurrentDictionary<string, Lazy<LoggerInstance>> _instances = new();
        private const string Default = "Default";
        public static LoggerInstance Instance => GetInstance(Default);

        public static LoggerInstance GetInstance(string name, LoggerConfig config = null)
        {
            name = string.IsNullOrEmpty(name) ? Default : name;

            return _instances.GetOrAdd(name, k =>
                new Lazy<LoggerInstance>(() =>
                    new LoggerInstance(config ?? LoggerConfig.LoadFromFile())
                )
            ).Value;
        }
        #endregion

        private readonly LoggerConfig _config;
        private readonly ConcurrentDictionary<string, LogTemplate> _templates = new();
        private readonly Channel<LogMessage> _channel;
        private readonly List<ILogOutput> _outputs = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task[] _workers = Array.Empty<Task>();
        private bool _disposed;

        public LoggerInstance(LoggerConfig config)
        {
            _config = config;

            _channel = Channel.CreateBounded<LogMessage>(new BoundedChannelOptions(_config.MaxQueueSize) { FullMode = BoundedChannelFullMode.DropOldest });

            switch (_config.UseMemoryMappedType)
            {
                case LogOutputType.Console:
                    _outputs.Add(new OutputConsole(_config));
                    break;
                case LogOutputType.File:
                    _outputs.Add(new OutputFileStream(_config));
                    break;
                case LogOutputType.MMF:
                    _outputs.Add(new OutputMemoryMapped(_config));
                    break;
                case LogOutputType.FastSafe:
                    _outputs.Add(new OutputFastSafe(_config));
                    break;
                default:
                    _outputs.Add(new OutputConsole(_config));
                    break;
            }

            AddTemplate(new LogTemplate { Name = "Default", Level = LogLevel.Information, IncludeException = true });

            if (_config.EnableAsyncWriting)
            {
                _workers = Enumerable.Range(0, _config.MaxDegreeOfParallelism).Select(_ => Task.Factory.StartNew(ConsumeAsync, TaskCreationOptions.LongRunning)).ToArray();
            }
        }

        #region 日志核心
        public void Log<T>(LogLevel level, T state, Func<T, Exception, string>? formatter = null, Exception? ex = null, string? template = null)
        {
            if (_disposed) return;
            var tpl = GetTemplate(template);
            if (level < tpl.Level) return;

            string msgText;
            if (formatter != null)
            {
                msgText = formatter(state, ex);
            }
            else if (state is byte[] bytes)
            {
                msgText = Encoding.UTF8.GetString(bytes);
            }
            else if (state is ReadOnlyMemory<byte> rom)
            {
                msgText = Encoding.UTF8.GetString(rom.Span);
            }
            else
            {
                msgText = state?.ToString() ?? string.Empty;
            }

            var log = new LogMessage(
                DateTime.UtcNow.ToLocalTime(), 
                level,
                msgText,
                Environment.CurrentManagedThreadId,
                Thread.CurrentThread.Name?? string.Empty,
                ex
            );

            if (_config.EnableAsyncWriting)
                _channel.Writer.TryWrite(log);
            else
                WriteToOutputs(log);
        }

        private async Task ConsumeAsync()
        {
            await foreach (var msg in _channel.Reader.ReadAllAsync(_cts.Token))
                WriteToOutputs(msg);
        }

        private void WriteToOutputs(LogMessage msg)
        {
            foreach (var o in _outputs) o.Write(msg);
        }
        #endregion

        #region 模板
        public void AddTemplate(LogTemplate t) => _templates[t.Name] = t;
        public void RemoveTemplate(string n) => _templates.TryRemove(n, out _);
        public LogTemplate GetTemplate(string? n) => _templates.TryGetValue(n ?? "Default", out var t) ? t : _templates["Default"];
        #endregion

        #region ILogger
        public void Trace<T>(T s, Func<T, Exception, string>? f = null, string? t = null) => Log(LogLevel.Trace, s, f, null, t);
        public void Debug<T>(T s, Func<T, Exception, string>? f = null, string? t = null) => Log(LogLevel.Debug, s, f, null, t);
        public void Info<T>(T s, Func<T, Exception, string>? f = null, string? t = null) => Log(LogLevel.Information, s, f, null, t);
        public void Warn<T>(T s, Exception? e = null, Func<T, Exception, string>? f = null, string? t = null) => Log(LogLevel.Warning, s, f, e, t);
        public void Error<T>(T s, Exception? e = null, Func<T, Exception, string>? f = null, string? t = null) => Log(LogLevel.Error, s, f, e, t);
        public void Critical<T>(T s, Exception? e = null, Func<T, Exception, string>? f = null, string? t = null) => Log(LogLevel.Critical, s, f, e, t);
        #endregion

        #region 释放
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            Task.WaitAll(_workers, 10000);
            while (_channel.Reader.TryRead(out var m)) WriteToOutputs(m);
            foreach (var o in _outputs) { o.Flush(); o.Close(); }
            _cts.Dispose();
        }
        #endregion
    }

}