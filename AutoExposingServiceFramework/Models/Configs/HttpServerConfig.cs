namespace AutoExposingServiceFramework.Models.Configs;

public class HttpServerConfig
{
    public bool Enabled { get; set; } = true;
    public string ListenUrls { get; set; } = "http://localhost:5000";
    public bool EnableSwagger { get; set; } = true;
    public int ShutdownTimeoutSeconds { get; set; } = 10;
}
