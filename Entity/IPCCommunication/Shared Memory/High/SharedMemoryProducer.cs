using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Shared_Memory.High
{
    class SharedMemoryProducer
    {
        static async Task Main(string[] args)
        {
            const string sharedMemoryName = "HighPerformanceSharedMemoryDemo";
            const int bufferSize = 1024 * 1024; // 1MB

            // 创建共享内存
            using var sharedMemory = new HighPerformanceSharedMemory(sharedMemoryName);
            if (!sharedMemory.CreateOrOpen(bufferSize))
            {
                Console.WriteLine("创建共享内存失败");
                return;
            }

            Console.WriteLine("共享内存已创建，按Enter开始发送数据，输入'exit'退出...");
            Console.ReadLine();

            int messageCount = 0;
            while (true)
            {
                string message = $"Message {++messageCount} at {DateTime.Now:HH:mm:ss.fff}";
                byte[] data = Encoding.UTF8.GetBytes(message);

                // 写入数据（使用锁确保线程安全）
                if (sharedMemory.TryAcquireLock())
                {
                    try
                    {
                        if (sharedMemory.Write(data))
                        {
                            Console.WriteLine($"已发送: {message}");
                        }
                        else
                        {
                            Console.WriteLine("写入失败");
                        }
                    }
                    finally
                    {
                        sharedMemory.ReleaseLock();
                    }
                }
                else
                {
                    Console.WriteLine("获取锁超时");
                }

                // 等待用户输入或自动发送
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape)
                    break;

                await Task.Delay(1000); // 每秒发送一次
            }
        }
    }
}
