namespace AutoExposingServiceFramework.Models.Configs;

/// <summary>
/// 服务配置
/// </summary>
public class ServiceConfig
{
    public ServiceBaseConfig Base { get; set; } = new();
    public HttpServerConfig HttpServer { get; set; } = new();
}
