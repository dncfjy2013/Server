using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.NamedPipe
{
    class PipeClientDemo
    {
        static async Task Main(string[] args)
        {
            // 创建客户端（连接到本地服务器的"MyUniversalPipe"管道）
            using var client = new NamedPipeClient("MyUniversalPipe", ".");

            // 注册状态事件
            client.StateChanged += (state, msg) =>
            {
                Console.WriteLine($"[客户端状态] {state}: {msg}");
            };

            // 注册数据接收事件
            client.DataReceived += (data) =>
            {
                Console.WriteLine($"[收到回复] {data}");
            };

            try
            {
                // 连接到服务器
                await client.ConnectAsync();

                // 发送消息
                string input;
                while ((input = Console.ReadLine()) != "exit")
                {
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        bool success = await client.SendAsync(input);
                        Console.WriteLine($"发送{(success ? "成功" : "失败")}");
                    }
                }

                // 断开连接
                await client.DisconnectAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"客户端错误: {ex.Message}");
            }
        }
    }
}
