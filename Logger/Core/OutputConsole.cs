using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Logger.Core
{
    public class OutputConsole : ILogOutput
    {
        private readonly LoggerConfig _config;
        private static readonly object _consoleLock = new object();

        public OutputConsole(LoggerConfig config)
        {
            _config = config;
            Console.OutputEncoding = Encoding.UTF8;
        }

        public void Write(LogMessage message)
        {
            if (message.Level < _config.ConsoleLogLevel)
                return;

            try
            {
                var text = LogMessageFormatter.Format(message);
                if (_config.EnableConsoleColor)
                {
                    lock (_consoleLock)
                    {
                        var color = Console.ForegroundColor;
                        Console.ForegroundColor = GetColor(message.Level);
                        Console.Write(text);
                        Console.ForegroundColor = color;
                    }
                }
                else
                {
                    lock (_consoleLock)
                        Console.Write(text);
                }
            }
            catch { }
        }

        private ConsoleColor GetColor(LogLevel level) => level switch
        {
            LogLevel.Critical => ConsoleColor.Red,
            LogLevel.Error => ConsoleColor.DarkRed,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Information => ConsoleColor.Green,
            LogLevel.Debug => ConsoleColor.Cyan,
            _ => ConsoleColor.Gray
        };

        public void Flush() { }
        public void Close() { }
    }
}
