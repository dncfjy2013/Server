using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Pipe
{
    class PipeClient
    {
        static async Task Main()
        {
            // 连接到命名管道服务器
            using (var client = new NamedPipeClientStream(".", "MyNamedPipe", PipeDirection.InOut))
            {
                try
                {
                    Console.WriteLine("连接到服务器...");
                    await client.ConnectAsync(5000); // 超时5秒
                    Console.WriteLine("已连接到服务器");

                    // 向服务器发送消息
                    string message = "Hello from Client!";
                    byte[] messageBytes = Encoding.UTF8.GetBytes(message);
                    await client.WriteAsync(messageBytes, 0, messageBytes.Length);
                    await client.FlushAsync();
                    Console.WriteLine("消息已发送");

                    // 读取服务器响应
                    byte[] buffer = new byte[1024];
                    int bytesRead = await client.ReadAsync(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"收到服务器响应: {response}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"连接或通信错误: {ex.Message}");
                }
            }
            Console.WriteLine("客户端已关闭");
        }
    }
}
