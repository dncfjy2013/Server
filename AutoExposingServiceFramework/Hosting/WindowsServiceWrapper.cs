using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AutoExposingServiceFramework.Core;
using AutoExposingServiceFramework.Core.Logging;
using AutoExposingServiceFramework.Interfaces;
using AutoExposingServiceFramework.Models.Configs;
using System.ServiceProcess;

namespace AutoExposingServiceFramework.Hosting;

/// <summary>
/// Windows服务包装器
/// </summary>
public class WindowsServiceWrapper : ServiceBase
{
    private IHost? _host;
    private readonly string _serviceName;

    public WindowsServiceWrapper(string serviceName)
    {
        _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
        ServiceName = serviceName;
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            _host = CreateHostBuilder(args).Build();
            _host.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            EventLog.WriteEntry($"服务启动失败: {ex.Message}", System.Diagnostics.EventLogEntryType.Error);
            Stop();
        }
    }

    protected override void OnStop()
    {
        if (_host != null)
        {
            try
            {
                _host.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                _host.Dispose();
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry($"服务停止失败: {ex.Message}", System.Diagnostics.EventLogEntryType.Error);
            }
        }
    }

    public IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                ConfigureDependencyInjection(context, services);
            });

    /// <summary>
    /// 配置依赖注入
    /// </summary>
    private void ConfigureDependencyInjection(HostBuilderContext context, IServiceCollection services)
    {
        // 配置
        var config = new ServiceConfig();
        context.Configuration.Bind(config);
        services.AddSingleton(config);
        services.AddSingleton(config.Base);
        services.AddSingleton(config.HttpServer);

        // 核心服务
        services.AddSingleton<ILogger, FileLogger>();
        
        // 业务服务（可替换为自定义实现）
        services.AddSingleton<IBusinessWorker, Business.SampleBusinessWorker>();
        
        // 通信服务
        if (config.HttpServer.Enabled)
            services.AddSingleton<ICommunicationServer, HttpCommunicationServer>();

        // 服务宿主
        services.AddHostedService<ServiceHost>();
    }
}
