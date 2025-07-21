using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.NamedPipe
{
    class PipeServerDemo
    {
        static async Task Main(string[] args)
        {
            // 创建服务器（管道名称："MyUniversalPipe"）
            using var server = new NamedPipeServer("MyUniversalPipe");

            // 注册状态事件
            server.StateChanged += (state, msg) =>
            {
                Console.WriteLine($"[服务器状态] {state}: {msg}");
            };

            // 注册数据接收事件
            server.DataReceived += (data) =>
            {
                Console.WriteLine($"[收到消息] {data}");
                // 收到消息后回复
                _ = server.SendAsync($"服务器已收到: {data}");
            };

            // 启动服务器
            Console.WriteLine("服务器启动，按Ctrl+C停止...");
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await server.StartAsync(cts.Token);
            Console.WriteLine("服务器已停止");
        }
    }
}
