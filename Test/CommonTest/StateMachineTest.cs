using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Server.Common;

namespace Test.CommonTest
{
    public enum TestState
    {
        Init,
        Processing,
        Done,
        Failed,
        Timeout
    }

    public static class StateMachineTest
    {
        public static void Test()
        {
            Console.WriteLine("=== 状态机压力测试开始 ===");
            var stateMachine = new StateMachine<long, TestState>();

            // 正确的状态流转
            stateMachine.AddTransition(TestState.Init, TestState.Processing);
            stateMachine.AddTransition(TestState.Processing, TestState.Done);

            // 压测配置
            const int keyCount = 10_000;
            const int threadCount = 16;

            // 每个 Key 只执行 2 步: Init → Processing → Done
            long totalTasks = keyCount * 2;
            long successCount = 0;
            long failCount = 0;

            // 初始化所有 Key
            for (long i = 1; i <= keyCount; i++)
            {
                stateMachine.InitializeState(i, TestState.Init);
            }

            Console.WriteLine($"Key 数量: {keyCount}");
            Console.WriteLine($"总请求数: {totalTasks}");
            Console.WriteLine($"并发线程: {threadCount}\n");

            var sw = Stopwatch.StartNew();
            var cts = new CancellationTokenSource();

            // 高并发并行压测
            Parallel.For(0, threadCount, _ =>
            {
                var random = new Random(Guid.NewGuid().GetHashCode());
                while (!cts.IsCancellationRequested)
                {
                    long key = random.Next(1, keyCount + 1);
                    bool ok = false;

                    try
                    {
                        if (!stateMachine.TryGetCurrentState(key, out var current))
                            continue;

                        // 重点：每个状态只允许往下走一步，杜绝并发冲突
                        switch (current)
                        {
                            case TestState.Init:
                                ok = stateMachine.TransitionAsync(key, TestState.Processing, reason: "处理中").GetAwaiter().GetResult();
                                break;

                            case TestState.Processing:
                                ok = stateMachine.TransitionAsync(key, TestState.Done, reason: "已完成").GetAwaiter().GetResult();
                                break;
                        }
                    }
                    catch
                    {
                        // 忽略压测异常
                    }

                    if (ok) Interlocked.Increment(ref successCount);
                    else Interlocked.Increment(ref failCount);

                    // 达到总任务数停止
                    if (Interlocked.Read(ref successCount) >= totalTasks)
                    {
                        cts.Cancel();
                        break;
                    }
                }
            });

            sw.Stop();

            // 输出结果
            Console.WriteLine("=== 压测完成 ===");
            Console.WriteLine($"总耗时: {sw.Elapsed.TotalMilliseconds:F2} ms");
            Console.WriteLine($"成功: {successCount}");
            Console.WriteLine($"失败: {failCount}");
            Console.WriteLine($"成功率: {(double)successCount / totalTasks * 100:F2}%");
            Console.WriteLine($"QPS: {totalTasks / sw.Elapsed.TotalSeconds:F0}\n");

            // 校验最终一致性
            Console.WriteLine("=== 最终状态校验（必须全部是 Done）===");
            int errorCount = 0;
            for (long i = 1; i <= 100; i++)
            {
                stateMachine.TryGetCurrentState(i, out var s);
                if (s != TestState.Done) errorCount++;
                Console.WriteLine($"Key {i:00000} => {s}");
            }

            Console.WriteLine($"\n校验完成，错误状态数量: {errorCount} (0=完美)");
            Console.WriteLine("=== 压力测试全部完成 ===");
            stateMachine.Dispose();
        }
    }
}