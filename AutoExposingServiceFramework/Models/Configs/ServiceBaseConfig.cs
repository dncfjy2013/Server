using System.IO;

namespace AutoExposingServiceFramework.Models.Configs;

public class ServiceBaseConfig
{
    public string ServiceName { get; set; } = "AutoExposeService";
    public string DisplayName { get; set; } = "自动接口暴露服务";
    public string Description { get; set; } = "自动暴露业务接口的微服务框架";
    public int PollingIntervalMs { get; set; } = 5000;
    public string WorkingDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "service_data");
    public string LogDirectory => Path.Combine(WorkingDirectory, "logs");
}
