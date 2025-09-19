using System;
using System.IO;
using AutoExposingServiceFramework.Interfaces;
using AutoExposingServiceFramework.Models.Configs;

namespace AutoExposingServiceFramework.Core.Logging;

/// <summary>
/// 文件日志实现
/// </summary>
public class FileLogger : ILogger
{
    private readonly string _logDirectory;
    private readonly object _lock = new();

    public FileLogger(ServiceBaseConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
            
        _logDirectory = config.LogDirectory;
        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"创建日志目录失败: {ex.Message}");
        }
    }

    public void Log(string message) => WriteLog("INFO", message);
    public void LogError(string errorMessage) => WriteLog("ERROR", errorMessage);
    public void LogWarning(string warningMessage) => WriteLog("WARNING", warningMessage);

    private void WriteLog(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                var logFile = Path.Combine(_logDirectory, $"service_{DateTime.Now:yyyyMMdd}.log");
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(logFile, logEntry);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"写入日志失败: {ex.Message}");
        }
    }
}
