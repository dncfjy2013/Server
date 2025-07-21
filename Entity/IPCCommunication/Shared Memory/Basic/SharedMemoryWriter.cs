using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Shared_Memory
{
    class SharedMemoryWriter
    {
        static void Main()
        {
            // 创建或打开共享内存（名称为"SharedMemoryDemo"，大小1024字节）
            using (var mmf = MemoryMappedFile.CreateOrOpen(
                "SharedMemoryDemo",
                1024,
                MemoryMappedFileAccess.ReadWrite))
            {
                // 创建事件用于同步（通知读取进程数据已更新）
                using (var eventWaitHandle = new EventWaitHandle(
                    false,
                    EventResetMode.AutoReset,
                    "SharedMemoryEvent"))
                {
                    Console.WriteLine("共享内存写入进程启动，按任意键发送数据...");
                    Console.ReadKey();

                    // 写入数据到共享内存
                    using (var accessor = mmf.CreateViewAccessor())
                    {
                        string message = "Hello from Shared Memory!";
                        byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
                        accessor.WriteArray(0, data, 0, data.Length);
                        Console.WriteLine($"已写入数据: {message}");
                    }

                    // 触发事件，通知读取进程
                    eventWaitHandle.Set();
                    Console.WriteLine("已通知读取进程，按任意键退出...");
                    Console.ReadKey();
                }
            }
        }
    }
}
