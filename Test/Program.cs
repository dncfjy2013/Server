using CoordinateSystem;
using log4net;
using log4net.Config;
using System;
using System.IO;
using Test;
using Test.CommonTest;

public class Program
{
    // 日志对象
    private static readonly ILog _log = LogManager.GetLogger(typeof(Program));

    public static void Main()
    {
        try
        {
            // 初始化日志配置
            XmlConfigurator.Configure(new FileInfo("log4net.config"));
            _log.Info("应用启动");
            Console.WriteLine("========== 测试程序控制台版本 ==========\n");

            // 主循环，支持多次选择操作
            while (true)
            {
                // 打印菜单
                ShowMenu();

                // 读取用户输入
                string input = Console.ReadLine()?.Trim() ?? string.Empty;

                // 根据输入执行对应功能
                switch (input)
                {
                    case "1":
                        Console.WriteLine("\n>>> 开始执行【日志性能测试】...");
                        _log.Info("开始执行日志性能测试");
                        LoggerPerformanceTest.Performance();
                        _log.Info("日志性能测试执行完成");
                        Console.WriteLine(">>> 【日志性能测试】执行完毕！\n");
                        break;

                    case "2":
                        Console.WriteLine("\n>>> 开始执行【坐标系统全量测试】...");
                        _log.Info("开始执行坐标系统测试");
                        CoordinateFullTest.RunAllTests();
                        _log.Info("坐标系统测试执行完成");
                        Console.WriteLine(">>> 【坐标系统全量测试】执行完毕！\n");
                        break;

                    case "3":
                        Console.WriteLine("\n>>> 开始执行【状态机压力测试开始】...");
                        _log.Info("开始执行全部测试");
                        StateMachineTest.Test();
                        _log.Info("全部测试执行完成");
                        Console.WriteLine(">>> 【状态机压力测试开始】执行完毕！\n");
                        break;

                    case "0":
                        // 退出程序
                        _log.Info("用户主动退出应用");
                        Console.WriteLine("\n程序已退出，按任意键关闭窗口...");
                        Console.ReadKey();
                        return;

                    default:
                        // 输入无效
                        Console.WriteLine("输入错误！请输入有效的数字（0-3）\n");
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Fatal("程序发生致命错误", ex);
            Console.WriteLine($"\n程序异常：{ex.Message}");
            Console.ReadKey();
        }
    }

    /// <summary>
    /// 显示控制台菜单
    /// </summary>
    private static void ShowMenu()
    {
        Console.WriteLine("请选择要执行的操作：");
        Console.WriteLine("  1 - 执行 日志性能测试");
        Console.WriteLine("  2 - 执行 坐标系统全量测试");
        Console.WriteLine("  3 - 执行 状态机压力测试开始");
        Console.WriteLine("  0 - 退出程序");
        Console.Write("请输入数字并按回车：");
    }
}