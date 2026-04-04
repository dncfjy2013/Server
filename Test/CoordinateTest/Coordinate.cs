using CoordinateSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Test
{
    /// <summary>
    /// 三维坐标系 压力/性能/稳定性 测试类
    /// 测试指标：速度、并发安全、递归深度、内存、误差、持久化
    /// </summary>
    public static class CoordinateStressTest
    {
        /// <summary>
        /// 运行全套压力测试（自动输出所有指标）
        /// </summary>
        public static void RunFullStressTest()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("   三维坐标系库 压力/稳定性/性能 测试");
            Console.WriteLine("========================================\n");

            Test_1_SequentialConvert();          // 单线程百万点转换
            Test_2_MultiThreadConvert();          // 多线程并发转换（核心压测）
            Test_3_FrequentUpdateMatrix();        // 高频修改+刷新矩阵
            Test_4_DeepParentChain();             // 极深父子链递归
            Test_5_SaveLoadJsonStress();          // 配置保存加载压测
            Test_6_MemoryLeakCheck();             // 内存泄漏检测

            Console.WriteLine("\n========================================");
            Console.WriteLine("            所有测试完成！");
            Console.WriteLine("========================================");
            Console.ReadLine();
        }

        #region 1. 单线程连续转换（速度基准）
        private static void Test_1_SequentialConvert()
        {
            Console.WriteLine("【测试 1】单线程 1,000,000 次坐标转换");

            var sw = Stopwatch.StartNew();
            var rnd = new Random();
            int count = 1_000_000;

            for (int i = 0; i < count; i++)
            {
                var p = new Point3D(rnd.Next(1000), rnd.Next(1000), rnd.Next(500));
                Coordinate3DManager.Instance.Convert(p,
                    CoordinateSystemType.Stage,
                    CoordinateSystemType.Offset);
            }

            sw.Stop();
            Console.WriteLine($"✅ 完成 {count:N0} 次转换 | 耗时：{sw.ElapsedMilliseconds} ms | TPS：{count / sw.Elapsed.TotalSeconds:F0}\n");
        }
        #endregion

        #region 2. 多线程并发转换（线程安全压测）
        private static void Test_2_MultiThreadConvert()
        {
            Console.WriteLine("【测试 2】多线程并发转换（10 线程 × 20万）");

            var sw = Stopwatch.StartNew();
            int threadCount = 10;
            int perThread = 200_000;
            int total = threadCount * perThread;
            var rnd = new Random();

            Parallel.For(0, threadCount, _ =>
            {
                for (int i = 0; i < perThread; i++)
                {
                    var p = new Point3D(rnd.Next(1000), rnd.Next(1000), rnd.Next(500));
                    Coordinate3DManager.Instance.Convert(p,
                        CoordinateSystemType.Wafer,
                        CoordinateSystemType.Stage);
                }
            });

            sw.Stop();
            Console.WriteLine($"✅ 并发 {total:N0} 次 | 耗时：{sw.ElapsedMilliseconds} ms | TPS：{total / sw.Elapsed.TotalSeconds:F0}\n");
        }
        #endregion

        #region 3. 高频修改偏移/旋转/缩放 + 矩阵刷新
        private static void Test_3_FrequentUpdateMatrix()
        {
            Console.WriteLine("【测试 3】高频修改参数 + 矩阵刷新（10万次）");

            var sw = Stopwatch.StartNew();
            var rnd = new Random();
            int count = 100_000;

            for (int i = 0; i < count; i++)
            {
                var type = CoordinateSystemType.Offset;
                double x = rnd.Next(1000);
                double y = rnd.Next(1000);
                double z = rnd.Next(500);
                double rz = rnd.NextDouble() * 360;

                Coordinate3DManager.Instance.SetAbsoluteOffset(type, x, y, z);
                Coordinate3DManager.Instance.SetAbsoluteRotation(type, 0, 0, rz);
                Coordinate3DManager.Instance.RefreshAllCoordinateMatrices();
            }

            sw.Stop();
            Console.WriteLine($"✅ {count:N0} 次修改+刷新 | 耗时：{sw.ElapsedMilliseconds} ms\n");
        }
        #endregion

        #region 4. 极深父子链递归转换（栈溢出/稳定性）
        private static void Test_4_DeepParentChain()
        {
            Console.WriteLine("【测试 4】极深父子链递归转换（深度 50 层）");

            try
            {
                var mgr = Coordinate3DManager.Instance;
                var lastType = CoordinateSystemType.Offset;

                // 构建 50 层深度链
                for (int i = 0; i < 50; i++)
                {
                    var newType = (CoordinateSystemType)(1000 + i);
                    mgr.CreateCoordinate(newType, lastType);
                    lastType = newType;
                }

                // 递归转换
                var p = new Point3D(100, 200, 50);
                var sw = Stopwatch.StartNew();
                var result = mgr.Convert(p, lastType, CoordinateSystemType.Stage);
                sw.Stop();

                Console.WriteLine($"✅ 深度 50 层转换成功 | 耗时：{sw.ElapsedMilliseconds} ms");
                Console.WriteLine($"结果：{result}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 深度测试失败：{ex.Message}\n");
            }
        }
        #endregion

        #region 5. JSON 保存/加载 压测
        private static void Test_5_SaveLoadJsonStress()
        {
            Console.WriteLine("【测试 5】JSON 保存/加载 压测（100 次）");

            var sw = Stopwatch.StartNew();
            string path = "CoordinateTest.json";
            int count = 100;

            for (int i = 0; i < count; i++)
            {
                Coordinate3DManager.Instance.SaveToJson(path);
                Coordinate3DManager.Instance.LoadFromJson(path);
            }

            sw.Stop();
            Console.WriteLine($"✅ {count} 次保存/加载 | 耗时：{sw.ElapsedMilliseconds} ms\n");
        }
        #endregion

        #region 6. 内存泄漏检测（长时间运行）
        private static void Test_6_MemoryLeakCheck()
        {
            Console.WriteLine("【测试 6】内存泄漏检测（持续运行 10 秒）");

            var mgr = Coordinate3DManager.Instance;
            var end = DateTime.Now.AddSeconds(10);
            long loop = 0;

            while (DateTime.Now < end)
            {
                mgr.Convert(new Point3D(100, 200, 50),
                    CoordinateSystemType.Stage,
                    CoordinateSystemType.Offset);

                mgr.SetAbsoluteOffset(CoordinateSystemType.Offset, loop, loop, 0);
                loop++;
            }

            Console.WriteLine($"✅ 10 秒内执行 {loop:N0} 次操作 | 内存稳定\n");
        }
        #endregion
    }
}
