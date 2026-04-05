using System.Diagnostics;
using Logger;
using Logger;

/// <summary>
/// 日志组件性能测试（FileStream VS MemoryMapped）
/// </summary>
public class LoggerPerformanceTest
{
    // 测试配置
    private const int TEST_LOG_COUNT = 100000;     // 单线程测试日志条数
    private const int MULTI_THREAD_COUNT = 8;      // 多线程并发数
    private const int MULTI_TEST_LOG_COUNT = 50000;// 单线程多线程测试日志条数
    private static readonly string TestLogDir = "TestLogs";

    public static void Performance()
    {
        Console.WriteLine("========== 日志组件性能测试 ==========\n");
        Console.WriteLine($"测试场景：{TEST_LOG_COUNT:N0} 条单线程日志 | {MULTI_THREAD_COUNT} 线程并发");
        Console.WriteLine("=====================================\n");

        // 1. 测试普通文件流写入
        TestFileStreamOutput();

        Console.WriteLine("\n-------------------------------------\n");

        // 2. 测试内存映射文件写入
        TestMemoryMappedOutput();

        // 3. 清理测试文件
        //try { if (Directory.Exists(TestLogDir)) Directory.Delete(TestLogDir, true); } catch { }
        Console.WriteLine("\n测试完成，临时日志已清理！");
        Console.ReadKey();
    }

    #region 普通文件流性能测试
    static void TestFileStreamOutput()
    {
        Console.WriteLine("【测试开始】普通 FileStream 日志写入");
        var config = new LoggerConfig
        {
            LogDirectory = TestLogDir,
            UseMemoryMappedType = LogOutputType.File,       // 关闭MMF，使用文件流
            EnableAsyncWriting = true,         // 开启异步写入
            MaxDegreeOfParallelism = 8,
            File_Buffer_Size = 65536,
            File_Split_Size = 1024 * 1024 * 100
        };

        RunTest(config, "FileStream");
    }
    #endregion

    #region 内存映射文件性能测试
    static void TestMemoryMappedOutput()
    {
        Console.WriteLine("【测试开始】内存映射 MMF 日志写入");
        var config = new LoggerConfig
        {
            LogDirectory = TestLogDir,
            UseMemoryMappedType = LogOutputType.MMF,        // 开启MMF
            EnableAsyncWriting = true,
            MaxDegreeOfParallelism = 8,
            MMF_BUFFER_SIZE = 5 * 1024 * 1024,
            MMF_FLUSH_THRESHOLD = 1 * 1024 * 1024,
            MMF_Split_Size = 64 * 1024
        };

        RunTest(config, "MemoryMapped");
    }
    #endregion

    #region 统一测试逻辑
    static void RunTest(LoggerConfig config, string testName)
    {
        // 清空旧日志
        if (Directory.Exists(config.LogDirectory))
            Directory.Delete(config.LogDirectory, true);

        var logger = LoggerInstance.GetInstance(testName, config);
        var sw = Stopwatch.StartNew();
        var cpuWatch = new Stopwatch();

        try
        {
            // 测试1：单线程写入
            cpuWatch.Start();
            sw.Restart();
            for (int i = 0; i < TEST_LOG_COUNT; i++)
            {
                logger.LogInformation($"性能测试日志 - 普通日志内容 - 序号：{i} - 这是一条标准长度的日志消息");
            }
            logger.Dispose(); // 强制刷盘
            cpuWatch.Stop();

            var singleThreadMs = sw.ElapsedMilliseconds;
            var singleCpuMs = cpuWatch.ElapsedMilliseconds;
            var singleTps = TEST_LOG_COUNT * 1000.0 / singleThreadMs;

            Console.WriteLine($"┌─ 单线程测试完成");
            Console.WriteLine($"│  总耗时：{singleThreadMs} ms");
            Console.WriteLine($"│  CPU耗时：{singleCpuMs} ms");
            Console.WriteLine($"│  吞吐量：{singleTps:N0} 条/秒");

            // 等待资源释放
            Thread.Sleep(100);
            logger = LoggerInstance.GetInstance($"{testName}_Multi", config);
            cpuWatch.Reset();

            // 测试2：多线程并发写入
            cpuWatch.Start();
            sw.Restart();
            var threads = new List<Thread>();
            for (int t = 0; t < MULTI_THREAD_COUNT; t++)
            {
                var thread = new Thread(() =>
                {
                    for (int i = 0; i < MULTI_TEST_LOG_COUNT; i++)
                    {
                        logger.LogInformation($"多线程性能测试 - 线程{Thread.CurrentThread.ManagedThreadId} - 序号：{i}");
                    }
                });
                threads.Add(thread);
                thread.Start();
            }

            // 等待所有线程完成
            foreach (var t in threads) t.Join();
            logger.Dispose();
            cpuWatch.Stop();

            var totalLogCount = MULTI_THREAD_COUNT * MULTI_TEST_LOG_COUNT;
            var multiThreadMs = sw.ElapsedMilliseconds;
            var multiCpuMs = cpuWatch.ElapsedMilliseconds;
            var multiTps = totalLogCount * 1000.0 / multiThreadMs;

            Console.WriteLine($"├─ 多线程测试完成");
            Console.WriteLine($"│  总日志数：{totalLogCount:N0} 条");
            Console.WriteLine($"│  总耗时：{multiThreadMs} ms");
            Console.WriteLine($"│  CPU耗时：{multiCpuMs} ms");
            Console.WriteLine($"│  并发吞吐量：{multiTps:N0} 条/秒");
            Console.WriteLine($"└───────────────────────────────");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试异常：{ex.Message}");
        }
    }
    #endregion
}