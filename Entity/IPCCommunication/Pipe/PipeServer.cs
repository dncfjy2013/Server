using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Pipe
{
    class PipeServer
    {
        static async Task Main()
        {
            // 创建命名管道服务器，管道名称格式：@"\\.\pipe\管道名"
            using (var server = new NamedPipeServerStream(
                "MyNamedPipe",          // 管道名称
                PipeDirection.InOut,    // 双向通信
                NamedPipeServerStream.MaxAllowedServerInstances)) // 允许最大连接数

            {
                Console.WriteLine("等待客户端连接...");
                await server.WaitForConnectionAsync(); // 等待客户端连接
                Console.WriteLine("客户端已连接");

                try
                {
                    // 读取客户端数据
                    byte[] buffer = new byte[1024];
                    int bytesRead = await server.ReadAsync(buffer, 0, buffer.Length);
                    string clientMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Console.WriteLine($"收到客户端消息: {clientMessage}");

                    // 向客户端发送响应
                    string response = "服务器已收到消息：" + clientMessage;
                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await server.WriteAsync(responseBytes, 0, responseBytes.Length);
                    await server.FlushAsync();
                    Console.WriteLine("响应已发送");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"通信错误: {ex.Message}");
                }
            }
            Console.WriteLine("管道已关闭");
        }
    }
}
