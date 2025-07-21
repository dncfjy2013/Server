using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Info
{
    class EventProducer
    {
        static void Main()
        {
            // 创建或打开命名的手动重置事件
            using (var dataReadyEvent = new EventWaitHandle(
                false, EventResetMode.ManualReset, "Global\\DataReadyEvent"))
            using (var mmf = MemoryMappedFile.CreateOrOpen("Global\\DataMemory", 4096))
            using (var accessor = mmf.CreateViewAccessor())
            {
                string data = "需要处理的数据：12345";
                byte[] buffer = System.Text.Encoding.UTF8.GetBytes(data);
                accessor.WriteArray(0, buffer, 0, buffer.Length);
                Console.WriteLine("数据已准备就绪，通知消费者...");

                // 触发事件
                dataReadyEvent.Set();

                Console.ReadLine();
            }
        }
    }

    class EventConsumer
    {
        static void Main()
        {
            // 打开命名的手动重置事件
            using (var dataReadyEvent = EventWaitHandle.OpenExisting("Global\\DataReadyEvent"))
            using (var mmf = MemoryMappedFile.OpenExisting("Global\\DataMemory"))
            using (var accessor = mmf.CreateViewAccessor())
            {
                Console.WriteLine("等待数据准备...");

                // 等待事件触发
                dataReadyEvent.WaitOne();

                byte[] buffer = new byte[100];
                accessor.ReadArray(0, buffer, 0, buffer.Length);
                string data = System.Text.Encoding.UTF8.GetString(buffer).Trim('\0');
                Console.WriteLine($"收到数据：{data}，开始处理...");

                // 手动重置事件
                ((ManualResetEvent)dataReadyEvent).Reset();
            }
        }
    }
}
