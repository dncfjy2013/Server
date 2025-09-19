using System.Threading;
using System.Threading.Tasks;

namespace AutoExposingServiceFramework.Interfaces;

/// <summary>
/// 通信服务接口
/// </summary>
public interface ICommunicationServer
{
    string ServerName { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
    bool IsRunning { get; }
}
