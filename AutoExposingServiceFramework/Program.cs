using System.ServiceProcess;
using AutoExposingServiceFramework.Hosting;
using AutoExposingServiceFramework.Models.Configs;
using AutoExposingServiceFramework.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AutoExposingServiceFramework;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            // 加载配置
            var config = new ServiceConfig();
            new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddCommandLine(args)
                .Build()
                .Bind(config);

            // 处理命令行参数
            if (args.Length > 0)
            {
                ServiceManager.ExecuteCommand(args[0], config.Base);
                return;
            }

            // 运行服务
            if (ServiceManager.IsRunningAsAdmin())
            {
                // 作为Windows服务运行
                ServiceBase.Run(new WindowsServiceWrapper(config.Base.ServiceName));
            }
            else
            {
                // 控制台模式运行
                Console.WriteLine("控制台模式运行，按Ctrl+C停止");
                using var host = new WindowsServiceWrapper(config.Base.ServiceName)
                    .CreateHostBuilder(args)
                    .Build();
                
                host.Run();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"程序运行出错: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
