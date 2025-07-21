using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Shared_Memory.High
{
    class SharedMemoryConsumer
    {
        static void Main(string[] args)
        {
            const string sharedMemoryName = "HighPerformanceSharedMemoryDemo";

            // 打开共享内存
            using var sharedMemory = new HighPerformanceSharedMemory(sharedMemoryName);
            if (!sharedMemory.OpenExisting())
            {
                Console.WriteLine("打开共享内存失败");
                return;
            }

            Console.WriteLine("共享内存已打开，按ESC退出...");

            // 注册数据可用事件
            sharedMemory.DataAvailable += (sender, e) =>
            {
                // 获取锁并读取数据
                if (sharedMemory.TryAcquireLock())
                {
                    try
                    {
                        if (sharedMemory.Read(out byte[] data))
                        {
                            string message = Encoding.UTF8.GetString(data);
                            Console.WriteLine($"收到数据({e.DataLength} bytes): {message}");
                        }
                    }
                    finally
                    {
                        sharedMemory.ReleaseLock();
                    }
                }
            };

            // 主线程等待退出
            while (Console.ReadKey(true).Key != ConsoleKey.Escape)
            {
                // 继续等待
            }
        }
    }
}
