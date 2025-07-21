using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Info
{
    // 共享内存中的数据结构（需标记为可 blittable 类型，避免序列化）
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SharedData
    {
        public int Id; // 数据ID
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
        public string Message; // 消息内容
        public DateTime Timestamp; // 时间戳
    }

    class SemaphoreWriter
    {
        static void Main()
        {
            // 1. 创建命名信号量（初始计数1，最大计数1，全局名称）
            // 注意：.NET Framework 中需用 Semaphore 类，.NET Core+ 可用 SemaphoreSlim（但跨进程推荐 Semaphore）
            using (var semaphore = new Semaphore(
                initialCount: 1,       // 初始允许1个进程访问
                maximumCount: 1,       // 最大允许1个进程（独占）
                name: "Global\\MySemaphore"))
            // 2. 创建共享内存（大小为 SharedData 结构的字节数）
            using (var mmf = MemoryMappedFile.CreateOrOpen(
                "Global\\MySharedMemory",
                Marshal.SizeOf<SharedData>(),
                MemoryMappedFileAccess.ReadWrite))
            using (var accessor = mmf.CreateViewAccessor(
                0,
                Marshal.SizeOf<SharedData>(),
                MemoryMappedFileAccess.ReadWrite))
            {
                try
                {
                    // 3. 获取信号量（最多等待5秒，避免永久阻塞）
                    bool hasAccess = semaphore.WaitOne(5000);
                    if (!hasAccess)
                    {
                        Console.WriteLine("获取信号量超时，无法写入数据");
                        return;
                    }

                    // 4. 写入数据到共享内存
                    var data = new SharedData
                    {
                        Id = 1,
                        Message = "Hello from Writer Process!",
                        Timestamp = DateTime.Now
                    };
                    accessor.Write(0, ref data); // 直接写入结构体（高效）
                    Console.WriteLine($"已写入数据：ID={data.Id}, 消息={data.Message}");
                }
                finally
                {
                    // 5. 释放信号量（必须确保执行，否则其他进程无法访问）
                    semaphore.Release();
                    Console.WriteLine("已释放信号量");
                }
            }

            Console.ReadLine(); // 等待用户确认
        }
    }

    class SemaphoreReader
    {
        static void Main()
        {
            // 1. 打开已存在的命名信号量
            using (var semaphore = Semaphore.OpenExisting("Global\\MySemaphore"))
            // 2. 打开已存在的共享内存
            using (var mmf = MemoryMappedFile.OpenExisting(
                "Global\\MySharedMemory",
                MemoryMappedFileRights.Read))
            using (var accessor = mmf.CreateViewAccessor(
                0,
                Marshal.SizeOf<SharedData>(),
                MemoryMappedFileAccess.Read))
            {
                try
                {
                    // 3. 获取信号量（等待写入者释放）
                    bool hasAccess = semaphore.WaitOne(5000);
                    if (!hasAccess)
                    {
                        Console.WriteLine("获取信号量超时，无法读取数据");
                        return;
                    }

                    // 4. 从共享内存读取数据
                    SharedData data = new SharedData();
                    accessor.Read(0, ref data); // 直接读取结构体
                    Console.WriteLine($"读取到数据：");
                    Console.WriteLine($"ID: {data.Id}");
                    Console.WriteLine($"消息: {data.Message}");
                    Console.WriteLine($"时间: {data.Timestamp:yyyy-MM-dd HH:mm:ss}");
                }
                finally
                {
                    // 5. 释放信号量
                    semaphore.Release();
                    Console.WriteLine("已释放信号量");
                }
            }

            Console.ReadLine();
        }
    }
}
