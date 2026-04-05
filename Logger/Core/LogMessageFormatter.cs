using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logger.Core
{
    public static class LogMessageFormatter
    {
        private static readonly string[] _levelStrings = { "TRACE", "DEBUG", "INFO", "WARN", "ERROR", "CRITICAL" };
        private static readonly StringBuilderPool _sbPool = new StringBuilderPool(256, 100);

        public static string Format(LogMessage msg)
        {
            var sb = _sbPool.Get();
            try
            {
                sb.Append(msg.Timestamp.ToString("yyyy-MM-dd HH:mm:ss:fff"));
                sb.Append(' ');
                sb.Append('[');
                sb.Append(_levelStrings[(int)msg.Level]);
                sb.Append("] ");
                sb.Append('[');
                sb.Append(msg.ThreadId);
                sb.Append("] ");
                sb.Append(msg.Message);

                if (msg.Exception != null)
                {
                    sb.AppendLine();
                    sb.Append("【异常】");
                    sb.Append(msg.Exception.ToString());
                }

                sb.AppendLine();
                return sb.ToString();
            }
            finally
            {
                _sbPool.Return(sb);
            }
        }

        public class StringBuilderPool
        {
            private readonly ConcurrentQueue<StringBuilder> _pool;
            private readonly int _capacity;

            public StringBuilderPool(int capacity, int count)
            {
                _capacity = capacity;
                _pool = new ConcurrentQueue<StringBuilder>();
                for (int i = 0; i < count; i++)
                    _pool.Enqueue(new StringBuilder(capacity));
            }

            public StringBuilder Get() => _pool.TryDequeue(out var sb) ? sb : new StringBuilder(_capacity);
            public void Return(StringBuilder sb)
            {
                sb.Clear();
                _pool.Enqueue(sb);
            }
        }
    }
}
