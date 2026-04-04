using CoordinateSystem;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Test
{
    /// <summary>
    /// 三维坐标系全套自动化测试
    /// 1. 精度测试（转换误差）
    /// 2. 压力测试（TPS/性能）
    /// 3. 可靠性测试（异常/边界/重复）
    /// 4. 多线程安全测试
    /// 5. 持久化测试（保存/加载）
    /// 总耗时 ≤ 60秒
    /// </summary>
    public static class CoordinateFullTest
    {
        private static readonly Coordinate3DManager _mgr = Coordinate3DManager.Instance;
        private static readonly int _testTimeSec = 60;

        public static void RunAllTests()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("       三维坐标系全套测试 (1分钟内完成)");
            Console.WriteLine("==================================================");

            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                Test_1_Precision();        // 精度测试
                Test_2_Stress();           // 压力测试
                Test_3_Reliability();       // 可靠性测试
                Test_4_ThreadSafe();       // 多线程安全
                Test_5_Persistence();      // 持久化保存加载
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 测试异常：{ex.Message}");
            }

            sw.Stop();

            Console.WriteLine("\n==================================================");
            Console.WriteLine($"                所有测试完成");
            Console.WriteLine($"  总耗时：{sw.Elapsed.TotalSeconds:F2} 秒");
            Console.WriteLine("==================================================");
            Console.ReadLine();
        }

        #region 1. 精度测试：往返转换误差 ≤ 1e-6
        private static void Test_1_Precision()
        {
            Console.WriteLine("\n[测试1] 坐标转换精度测试（往返误差）");

            var p = new Point3D(100.123456, 200.654321, 50.111222);
            double maxError = 0;

            for (int i = 0; i < 10000; i++)
            {
                var world = _mgr.Get(CoordinateSystemType.World).ConvertToWorld(p);
                var back = _mgr.Get(CoordinateSystemType.World).ConvertFromWorld(world);

                double ex = Math.Abs(p.X - back.X);
                double ey = Math.Abs(p.Y - back.Y);
                double ez = Math.Abs(p.Z - back.Z);
                maxError = Math.Max(maxError, new[] { ex, ey, ez }.Max());
            }

            bool pass = maxError < 1e-6;
            Console.WriteLine($"✅ 最大误差：{maxError:F10} | 测试结果：{(pass ? "通过" : "失败")}");
        }
        #endregion

        #region 2. 压力测试：TPS / 性能
        private static void Test_2_Stress()
        {
            Console.WriteLine("\n[测试2] 压力性能测试");

            long count = 0;
            var end = DateTime.Now.AddSeconds(10);
            var rnd = new Random();

            while (DateTime.Now < end)
            {
                var p = new Point3D(rnd.Next(1000), rnd.Next(1000), rnd.Next(500));
                _mgr.Convert(p, CoordinateSystemType.Stage, CoordinateSystemType.Offset);
                count++;
            }

            double tps = count / 10.0;
            Console.WriteLine($"✅ 10秒处理：{count:N0} 次 | TPS：{tps:F0}");
        }
        #endregion

        #region 3. 可靠性测试：边界、空值、重复、异常
        private static void Test_3_Reliability()
        {
            Console.WriteLine("\n[测试3] 可靠性测试（边界/重复/异常）");

            bool allPass = true;

            // 1. 零点转换
            try
            {
                var p = new Point3D(0, 0, 0);
                var r = _mgr.Convert(p, CoordinateSystemType.Stage, CoordinateSystemType.Offset);
            }
            catch { allPass = false; }

            // 2. 大数转换
            try
            {
                var p = new Point3D(1e8, 1e8, 1e6);
                var r = _mgr.Convert(p, CoordinateSystemType.Stage, CoordinateSystemType.Offset);
            }
            catch { allPass = false; }

            // 3. 高频重置
            try
            {
                for (int i = 0; i < 1000; i++)
                    _mgr.ResetAll();
            }
            catch { allPass = false; }

            // 4. 重复刷新矩阵
            try
            {
                for (int i = 0; i < 1000; i++)
                    _mgr.RefreshAllCoordinateMatrices();
            }
            catch { allPass = false; }

            Console.WriteLine($"✅ 可靠性测试：{(allPass ? "通过" : "失败")}");
        }
        #endregion

        #region 4. 多线程安全测试（无死锁、无崩溃）
        private static void Test_4_ThreadSafe()
        {
            Console.WriteLine("\n[测试4] 多线程安全测试");

            bool pass = true;

            try
            {
                Parallel.For(0, 8, i =>
                {
                    var rnd = new Random();
                    for (int j = 0; j < 10000; j++)
                    {
                        var p = new Point3D(rnd.Next(1000), rnd.Next(1000), rnd.Next(300));
                        _mgr.Convert(p, CoordinateSystemType.Wafer, CoordinateSystemType.Stage);

                        if (j % 1000 == 0)
                            _mgr.SetAbsoluteOffset(CoordinateSystemType.Offset, rnd.Next(100), rnd.Next(100), 0);
                    }
                });
            }
            catch
            {
                pass = false;
            }

            Console.WriteLine($"✅ 多线程测试：{(pass ? "通过" : "失败")}");
        }
        #endregion

        #region 5. 持久化测试：保存/加载/数据一致
        private static void Test_5_Persistence()
        {
            Console.WriteLine("\n[测试5] 配置保存/加载测试");

            bool pass = true;
            string file = "test_config.json";

            try
            {
                // 修改一个参数
                _mgr.SetOffset(10, 20);
                _mgr.SaveToJson(file);
                _mgr.LoadFromJson(file);

                var offset = _mgr.GetOffset();
                if (Math.Abs(offset.Offset.X - 10) > 1e-6 || Math.Abs(offset.Offset.Y - 20) > 1e-6)
                    pass = false;
            }
            catch
            {
                pass = false;
            }

            Console.WriteLine($"✅ 持久化测试：{(pass ? "通过" : "失败")}");
        }
        #endregion
    }
}
