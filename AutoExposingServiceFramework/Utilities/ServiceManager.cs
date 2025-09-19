using System;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using AutoExposingServiceFramework.Models.Configs;

namespace AutoExposingServiceFramework.Utilities;

public static class ServiceManager
{
    /// <summary>
    /// 检查是否以管理员权限运行
    /// </summary>
    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    /// <summary>
    /// 检查服务是否已安装
    /// </summary>
    public static bool IsServiceInstalled(string serviceName)
    {
        if (string.IsNullOrEmpty(serviceName))
            return false;

        using var controller = new ServiceController(serviceName);
        try
        {
            _ = controller.Status;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 执行服务命令
    /// </summary>
    public static void ExecuteCommand(string command, ServiceBaseConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        if (!IsRunningAsAdmin() && command != "help")
        {
            Console.WriteLine("需要管理员权限执行此操作");
            return;
        }

        switch (command.ToLowerInvariant())
        {
            case "install":
                InstallService(config);
                break;
            case "uninstall":
                UninstallService(config);
                break;
            case "start":
                StartService(config);
                break;
            case "stop":
                StopService(config);
                break;
            case "restart":
                RestartService(config);
                break;
            case "status":
                CheckStatus(config);
                break;
            default:
                ShowHelp();
                break;
        }
    }

    private static void InstallService(ServiceBaseConfig config)
    {
        if (IsServiceInstalled(config.ServiceName))
        {
            Console.WriteLine($"服务 {config.DisplayName} 已安装");
            return;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.WriteLine("无法获取当前程序路径");
            return;
        }

        try
        {
            RunScCommand($"create {config.ServiceName} binPath= \"{exePath}\" start= auto");
            RunScCommand($"config {config.ServiceName} displayname= \"{config.DisplayName}\"");
            RunScCommand($"description {config.ServiceName} \"{config.Description}\"");
            Console.WriteLine($"服务 {config.DisplayName} 安装成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"服务安装失败: {ex.Message}");
        }
    }

    private static void UninstallService(ServiceBaseConfig config)
    {
        if (!IsServiceInstalled(config.ServiceName))
        {
            Console.WriteLine($"服务 {config.DisplayName} 未安装");
            return;
        }

        try
        {
            // 先停止服务
            if (GetServiceStatus(config.ServiceName) == ServiceControllerStatus.Running)
            {
                RunScCommand($"stop {config.ServiceName}");
                Thread.Sleep(2000); // 等待服务停止
            }

            RunScCommand($"delete {config.ServiceName}");
            Console.WriteLine($"服务 {config.DisplayName} 卸载成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"服务卸载失败: {ex.Message}");
        }
    }

    private static void StartService(ServiceBaseConfig config)
    {
        if (!IsServiceInstalled(config.ServiceName))
        {
            Console.WriteLine($"服务 {config.DisplayName} 未安装");
            return;
        }

        try
        {
            if (GetServiceStatus(config.ServiceName) == ServiceControllerStatus.Running)
            {
                Console.WriteLine($"服务 {config.DisplayName} 已在运行");
                return;
            }

            RunScCommand($"start {config.ServiceName}");
            Console.WriteLine($"服务 {config.DisplayName} 已启动");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"服务启动失败: {ex.Message}");
        }
    }

    private static void StopService(ServiceBaseConfig config)
    {
        if (!IsServiceInstalled(config.ServiceName))
        {
            Console.WriteLine($"服务 {config.DisplayName} 未安装");
            return;
        }

        try
        {
            var status = GetServiceStatus(config.ServiceName);
            if (status != ServiceControllerStatus.Running && status != ServiceControllerStatus.StartPending)
            {
                Console.WriteLine($"服务 {config.DisplayName} 未在运行");
                return;
            }

            RunScCommand($"stop {config.ServiceName}");
            Console.WriteLine($"服务 {config.DisplayName} 已停止");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"服务停止失败: {ex.Message}");
        }
    }

    private static void RestartService(ServiceBaseConfig config)
    {
        try
        {
            StopService(config);
            Thread.Sleep(2000);
            StartService(config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"服务重启失败: {ex.Message}");
        }
    }

    private static void CheckStatus(ServiceBaseConfig config)
    {
        if (!IsServiceInstalled(config.ServiceName))
        {
            Console.WriteLine("服务未安装");
            return;
        }

        try
        {
            var status = GetServiceStatus(config.ServiceName);
            Console.WriteLine($"服务状态: {status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取服务状态失败: {ex.Message}");
        }
    }

    private static ServiceControllerStatus GetServiceStatus(string serviceName)
    {
        using var controller = new ServiceController(serviceName);
        return controller.Status;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("可用命令:");
        Console.WriteLine("  install   - 安装Windows服务");
        Console.WriteLine("  uninstall - 卸载Windows服务");
        Console.WriteLine("  start     - 启动服务");
        Console.WriteLine("  stop      - 停止服务");
        Console.WriteLine("  restart   - 重启服务");
        Console.WriteLine("  status    - 查看服务状态");
        Console.WriteLine("  help      - 显示帮助信息");
    }

    private static void RunScCommand(string args)
    {
        using var process = Process.Start(new ProcessStartInfo("sc.exe", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("无法启动sc.exe进程");

        process.WaitForExit();
        
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        if (!string.IsNullOrEmpty(error))
            Console.WriteLine($"命令输出: {error}");
            
        if (process.ExitCode != 0)
        {
            throw new Exception($"命令执行失败 (代码: {process.ExitCode}): sc {args}");
        }
    }
}
