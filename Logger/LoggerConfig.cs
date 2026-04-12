using System;
using System.IO;
using System.Xml.Serialization;

namespace Logger
{
    [XmlRoot("LoggerConfig")]
    public class LoggerConfig
    {
        public string LogName { get; set; } = "DefaultName";
        public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Trace;
        public LogLevel FileLogLevel { get; set; } = LogLevel.Trace;
        public string LogDirectory { get; set; } = "Logs";
        public string LogFileNameFormat { get; set; } = "Log_{0:yyyyMMdd}_{1:D3}.dat";
        public bool EnableAsyncWriting { get; set; } = true;
        public int MaxQueueSize { get; set; } = int.MaxValue;
        public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
        public LogOutputType UseMemoryMappedType { get; set; } = LogOutputType.Console;

        public bool EnableConsoleColor { get; set; } = true;

        public int Flush_Interval { get; set; } = 500;
        public int File_Buffer_Size { get; set; } = 64 * 1024;
        public long File_Split_Size { get; set; } = 100L * 1024 * 1024;

        public long MMF_BUFFER_SIZE { get; set; } = 5 * 1024 * 1024;
        public long MMF_FLUSH_THRESHOLD { get; set; } = 1 * 1024 * 1024;
        public long MMF_Split_Size { get; set; } = 100L * 1024 * 1024;
        public string CACHE_FILE_NAME { get; set; } = "mmf_cache.tmp";

        /// <summary>
        /// 从XML加载配置
        /// </summary>
        /// <returns>绝不返回null</returns>
        public static LoggerConfig LoadFromFile(string filePath = "LoggerConfig.xml")
        {
            try
            {
                filePath = Path.GetFullPath(filePath);

                if (!File.Exists(filePath))
                {
                    LoggerConfig defaultConfig = new LoggerConfig();
                    defaultConfig.SaveToFile(filePath);
                    return defaultConfig;
                }

                using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                XmlSerializer serializer = new XmlSerializer(typeof(LoggerConfig));
                LoggerConfig? config = serializer.Deserialize(fs) as LoggerConfig;

                return config ?? new LoggerConfig();
            }
            catch
            {
                return new LoggerConfig();
            }
        }

        /// <summary>
        /// 保存到XML
        /// </summary>
        public void SaveToFile(string filePath = "LoggerConfig.xml")
        {
            try
            {
                filePath = Path.GetFullPath(filePath);
                string? directory = Path.GetDirectoryName(filePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                XmlSerializer serializer = new XmlSerializer(typeof(LoggerConfig));
                using FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                serializer.Serialize(fs, this);
            }
            catch
            {
                throw;
            }
        }
    }
}