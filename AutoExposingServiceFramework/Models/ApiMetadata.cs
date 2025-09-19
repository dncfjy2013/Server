using System;
using System.Reflection;

namespace AutoExposingServiceFramework.Models;

/// <summary>
/// 接口元数据（反射获取的接口信息）
/// </summary>
public class ApiMetadata
{
    public string Path { get; set; } = "";
    public string HttpMethod { get; set; } = "POST";
    public string Description { get; set; } = "";
    public MethodInfo Method { get; set; } = null!;
    public Type[] ParameterTypes { get; set; } = Array.Empty<Type>();
    public Type ReturnType { get; set; } = typeof(void);
}
