using System.Threading;
using System.Threading.Tasks;

namespace AutoExposingServiceFramework.Interfaces;

/// <summary>
/// 业务工作器接口（核心业务逻辑）
/// </summary>
public interface IBusinessWorker
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync();
}
