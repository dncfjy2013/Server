namespace AutoExposingServiceFramework.Interfaces;

/// <summary>
/// 日志接口
/// </summary>
public interface ILogger
{
    void Log(string message);
    void LogError(string errorMessage);
    void LogWarning(string warningMessage);
}
