using System;
using System.Threading;
using System.Threading.Tasks;
using AutoExposingServiceFramework.Attributes;
using AutoExposingServiceFramework.Interfaces;
using AutoExposingServiceFramework.Models.Configs;

namespace AutoExposingServiceFramework.Business;

/// <summary>
/// 示例业务工作器
/// 展示如何通过特性自动暴露接口
/// </summary>
public class SampleBusinessWorker : IBusinessWorker
{
    private readonly ILogger _logger;
    private readonly ServiceBaseConfig _config;
    private bool _isWorking;
    private int _counter = 0;

    public SampleBusinessWorker(ILogger logger, ServiceBaseConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _isWorking = true;
        _logger.Log("业务工作器启动");

        // 启动后台任务
        _ = Task.Run(async () =>
        {
            while (_isWorking && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _counter++;
                    _logger.Log($"后台任务运行中，计数: {_counter}");
                    await Task.Delay(_config.PollingIntervalMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不记录错误
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"后台任务出错: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _isWorking = false;
        _logger.Log("业务工作器停止");
        return Task.CompletedTask;
    }

    #region 暴露的接口示例（通过特性标记）

    /// <summary>
    /// 获取当前计数器值
    /// </summary>
    [ExposeApi("/counter/get", HttpMethod = "GET", Description = "获取当前计数器值")]
    public Task<int> GetCounterValueAsync()
    {
        return Task.FromResult(_counter);
    }

    /// <summary>
    /// 重置计数器
    /// </summary>
    [ExposeApi("/counter/reset", HttpMethod = "POST", Description = "重置计数器为0")]
    public Task ResetCounterAsync()
    {
        _counter = 0;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 加法运算
    /// </summary>
    [ExposeApi("/math/add", HttpMethod = "GET", Description = "执行加法运算")]
    public Task<int> AddNumbersAsync(int a, int b)
    {
        return Task.FromResult(a + b);
    }

    /// <summary>
    /// 处理复杂对象
    /// </summary>
    [ExposeApi("/data/process", HttpMethod = "POST", Description = "处理复杂数据对象")]
    public Task<DataResponse> ProcessDataAsync(DataRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
            
        if (string.IsNullOrEmpty(request.Name))
            throw new ArgumentException("请求名称不能为空", nameof(request));

        return Task.FromResult(new DataResponse
        {
            Success = true,
            Message = $"处理成功: {request.Name}",
            ProcessedTime = DateTime.Now,
            ResultData = request.Value * 2
        });
    }

    #endregion
}
