using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoExposingServiceFramework.Interfaces;
using Microsoft.Extensions.Hosting;

namespace AutoExposingServiceFramework.Core;

/// <summary>
/// 服务宿主
/// </summary>
public class ServiceHost : BackgroundService
{
    private readonly ILogger _logger;
    private readonly IBusinessWorker _businessWorker;
    private readonly IEnumerable<ICommunicationServer> _communicationServers;
    private readonly CancellationTokenSource _cts = new();
    private bool _isRunning;

    public ServiceHost(ILogger logger, 
                      IBusinessWorker businessWorker, 
                      IEnumerable<ICommunicationServer> communicationServers)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _businessWorker = businessWorker ?? throw new ArgumentNullException(nameof(businessWorker));
        _communicationServers = communicationServers ?? throw new ArgumentNullException(nameof(communicationServers));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Log("服务启动中...");
        _isRunning = true;

        // 关联取消令牌
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cts.Token);
        var linkedToken = linkedCts.Token;

        try
        {
            // 启动通信服务
            foreach (var server in _communicationServers)
            {
                await RetryAsync(
                    () => server.StartAsync(linkedToken),
                    2,
                    1000,
                    linkedToken,
                    _logger,
                    $"启动通信服务 [{server.ServerName}]"
                );
            }

            // 启动业务逻辑
            await _businessWorker.StartAsync(linkedToken);

            // 保持运行
            while (_isRunning && !linkedToken.IsCancellationRequested)
            {
                await Task.Delay(1000, linkedToken).WaitAsync(linkedToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Log("服务被正常取消");
        }
        catch (Exception ex)
        {
            _logger.LogError($"服务运行出错: {ex.Message}（堆栈：{ex.StackTrace}）");
            throw;
        }
        finally
        {
            // 停止业务逻辑
            try
            {
                await _businessWorker.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"停止业务逻辑出错: {ex.Message}");
            }

            // 停止通信服务
            foreach (var server in _communicationServers)
            {
                try
                {
                    if (server.IsRunning)
                    {
                        await server.StopAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"停止通信服务 [{server.ServerName}] 出错: {ex.Message}");
                }
            }

            _isRunning = false;
            _logger.Log("服务已停止");
        }
    }

    /// <summary>
    /// 带重试机制的异步操作执行
    /// </summary>
    private async Task RetryAsync(Func<Task> operation, int maxRetries, int delayMs, 
                                 CancellationToken token, ILogger logger, string operationName)
    {
        int attempts = 0;
        while (true)
        {
            attempts++;
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (attempts < maxRetries)
            {
                logger.LogWarning($"{operationName} 第 {attempts} 次尝试失败: {ex.Message}，将重试...");
                await Task.Delay(delayMs, token);
            }
        }
    }
}
