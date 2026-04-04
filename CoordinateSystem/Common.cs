using CoordinateSystem;
using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

[assembly: LogAspect]
/// <summary>
/// 坐标系参数变更事件参数类
/// 用于传递坐标系发生修改时的类型与描述信息
/// </summary>
public class CoordinateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 发生变更的坐标系类型（世界/相机/机器/视觉等）
    /// </summary>
    public CoordinateSystemType SystemType { get; }

    /// <summary>
    /// 变更内容描述消息（偏移/旋转/缩放/补偿等具体操作）
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="type">发生变更的坐标系类型</param>
    /// <param name="message">变更内容的详细描述</param>
    public CoordinateChangedEventArgs(CoordinateSystemType type, string message)
    {
        SystemType = type;
        Message = message;
    }
}

public static class CoordLogger
{
    private static readonly ILog _log = LogManager.GetLogger("CoordinateSystem");

    public static void Info(string msg)
    {
        _log.Info($"[{DateTime.Now:HH:mm:ss}] INFO  {msg}");
    }
    public static void Warn(string msg) 
    { 
        _log.Warn($"[{DateTime.Now:HH:mm:ss}] WARN  {msg}");
    }
    public static void Error(string msg)
    { 
        _log.Error($"[{DateTime.Now:HH:mm:ss}] ERROR {msg}");
    }
}
