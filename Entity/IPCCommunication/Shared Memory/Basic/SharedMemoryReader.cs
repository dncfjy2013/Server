using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Shared_Memory
{
    class SharedMemoryReader
    {
        static void Main()
        {
            // 打开共享内存
            using (var mmf = MemoryMappedFile.OpenExisting(
                "SharedMemoryDemo",
                MemoryMappedFileRights.Read))
            {
                // 打开同步事件
                using (var eventWaitHandle = new EventWaitHandle(
                    false,
                    EventResetMode.AutoReset,
                    "SharedMemoryEvent"))
                {
                    Console.WriteLine("共享内存读取进程启动，等待数据...");

                    // 等待事件触发（数据已写入）
                    eventWaitHandle.WaitOne();

                    // 从共享内存读取数据
                    using (var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                    {
                        byte[] data = new byte[1024];
                        accessor.ReadArray(0, data, 0, data.Length);
                        string message = Encoding.UTF8.GetString(data).Trim('\0'); // 去除空字符
                        Console.WriteLine($"读取到数据: {message}");
                    }

                    Console.WriteLine("按任意键退出...");
                    Console.ReadKey();
                }
            }
        }
    }
}
