using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Info
{
    class MutexWriter
    {
        static void Main()
        {
            // 1. 创建命名互斥量（初始不拥有，跨进程可见）
            using (var mutex = new Mutex(false, "Global\\MyMutex"))
            // 2. 创建共享内存（4096字节，跨进程可见）
            using (var mmf = MemoryMappedFile.CreateOrOpen("Global\\MySharedMemory", 4096))
            using (var accessor = mmf.CreateViewAccessor())
            {
                try
                {
                    // 3. 等待互斥量（最多等5秒，获取锁）
                    if (mutex.WaitOne(5000))
                    {
                        // 4. 写入数据到共享内存（临界区）
                        string message = "Hello from Writer!";
                        byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
                        accessor.WriteArray(0, data, 0, data.Length);
                        Console.WriteLine("数据已写入共享内存");
                    }
                    else
                    {
                        Console.WriteLine("获取互斥量超时");
                    }
                }
                finally
                {
                    // 5. 释放互斥量（必须在finally中确保释放）
                    if (mutex.WaitOne(0)) // 检查是否拥有锁
                        mutex.ReleaseMutex();
                }
            }
        }
    }

    class MutexReader
    {
        static void Main()
        {
            // 1. 打开已存在的命名互斥量
            using (var mutex = Mutex.OpenExisting("Global\\MyMutex"))
            // 2. 打开已存在的共享内存
            using (var mmf = MemoryMappedFile.OpenExisting("Global\\MySharedMemory"))
            using (var accessor = mmf.CreateViewAccessor())
            {
                try
                {
                    // 3. 等待互斥量（获取锁）
                    if (mutex.WaitOne(5000))
                    {
                        // 4. 从共享内存读取数据（临界区）
                        byte[] data = new byte[100];
                        accessor.ReadArray(0, data, 0, data.Length);
                        string message = System.Text.Encoding.UTF8.GetString(data).Trim('\0');
                        Console.WriteLine($"读取到数据：{message}");
                    }
                    else
                    {
                        Console.WriteLine("获取互斥量超时");
                    }
                }
                finally
                {
                    // 5. 释放互斥量
                    if (mutex.WaitOne(0))
                        mutex.ReleaseMutex();
                }
            }
        }
    }
}
