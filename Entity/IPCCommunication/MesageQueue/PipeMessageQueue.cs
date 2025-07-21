using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.MesageQueue
{
    // 消息队列服务端（发送者）
    public sealed class PipeMessageQueueSender<T> : IDisposable where T : class
    {
        private readonly string _pipeName;
        private readonly NamedPipeServerStream _serverPipe;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Channel<T> _messageChannel = Channel.CreateUnbounded<T>();
        private readonly Task _processingTask;

        public PipeMessageQueueSender(string pipeName)
        {
            _pipeName = pipeName;
            _serverPipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.Out,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous);

            _processingTask = ProcessMessagesAsync();
        }

        // 发送消息
        public async Task SendAsync(T message)
        {
            await _messageChannel.Writer.WriteAsync(message);
        }

        private async Task ProcessMessagesAsync()
        {
            try
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    // 等待客户端连接
                    if (!_serverPipe.IsConnected)
                    {
                        Console.WriteLine($"等待客户端连接到管道 '{_pipeName}'...");
                        await _serverPipe.WaitForConnectionAsync(_cancellationTokenSource.Token);
                        Console.WriteLine("客户端已连接");
                    }

                    // 处理消息
                    await foreach (var message in _messageChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
                    {
                        try
                        {
                            var json = JsonSerializer.Serialize(message);
                            var bytes = Encoding.UTF8.GetBytes(json);

                            // 写入消息长度（4字节）
                            await _serverPipe.WriteAsync(BitConverter.GetBytes(bytes.Length));
                            // 写入消息内容
                            await _serverPipe.WriteAsync(bytes);
                            await _serverPipe.FlushAsync();
                        }
                        catch (IOException ex)
                        {
                            Console.WriteLine($"发送消息失败: {ex.Message}");
                            // 客户端断开连接，重置管道
                            _serverPipe.Disconnect();
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _processingTask?.Wait(1000);

            if (_serverPipe.IsConnected)
                _serverPipe.Disconnect();

            _serverPipe?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }

    // 消息队列客户端（接收者）
    public sealed class PipeMessageQueueReceiver<T> : IDisposable where T : class
    {
        private readonly string _pipeName;
        private readonly Channel<T> _messageChannel = Channel.CreateUnbounded<T>();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Task _receivingTask;

        public PipeMessageQueueReceiver(string pipeName)
        {
            _pipeName = pipeName;
            _receivingTask = ReceiveMessagesAsync();
        }

        // 接收消息流
        public IAsyncEnumerable<T> ReceiveAllAsync(CancellationToken cancellationToken = default)
            => _messageChannel.Reader.ReadAllAsync(cancellationToken);

        private async Task ReceiveMessagesAsync()
        {
            try
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        using var clientPipe = new NamedPipeClientStream(
                            ".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);

                        Console.WriteLine($"尝试连接到管道 '{_pipeName}'...");
                        await clientPipe.ConnectAsync(5000, _cancellationTokenSource.Token);
                        Console.WriteLine("已连接到管道");

                        while (clientPipe.IsConnected && !_cancellationTokenSource.IsCancellationRequested)
                        {
                            // 读取消息长度
                            var lengthBuffer = new byte[4];
                            var bytesRead = await clientPipe.ReadAsync(lengthBuffer, 0, 4, _cancellationTokenSource.Token);

                            if (bytesRead == 0) // 管道关闭
                                break;

                            if (bytesRead != 4)
                                throw new InvalidOperationException("读取消息长度失败");

                            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);

                            // 读取消息内容
                            var messageBuffer = new byte[messageLength];
                            var totalBytesRead = 0;

                            while (totalBytesRead < messageLength)
                            {
                                var read = await clientPipe.ReadAsync(
                                    messageBuffer,
                                    totalBytesRead,
                                    messageLength - totalBytesRead,
                                    _cancellationTokenSource.Token);

                                if (read == 0) // 管道关闭
                                    break;

                                totalBytesRead += read;
                            }

                            if (totalBytesRead == messageLength)
                            {
                                var json = Encoding.UTF8.GetString(messageBuffer);
                                var message = JsonSerializer.Deserialize<T>(json);

                                if (message != null)
                                    await _messageChannel.Writer.WriteAsync(message);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 正常关闭
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"接收消息错误: {ex.Message}，尝试重新连接...");
                        await Task.Delay(1000); // 等待后重试
                    }
                }
            }
            finally
            {
                _messageChannel.Writer.Complete();
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _receivingTask?.Wait(1000);
            _cancellationTokenSource?.Dispose();
        }
    }

    public class PipeMessageQueueTest
    {
        // 消息模型
        public class MyMessage
        {
            public int Id { get; set; }
            public string Content { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public async void test()
        {
            using var sender = new PipeMessageQueueSender<MyMessage>("MyMessageQueue");

            for (int i = 0; i < 10; i++)
            {
                await sender.SendAsync(new MyMessage
                {
                    Id = i,
                    Content = $"消息 {i}",
                    Timestamp = DateTime.Now
                });

                Console.WriteLine($"已发送消息 {i}");
                await Task.Delay(500);
            }

            using var receiver = new PipeMessageQueueReceiver<MyMessage>("MyMessageQueue");

            await foreach (var message in receiver.ReceiveAllAsync())
            {
                Console.WriteLine($"收到消息: ID={message.Id}, 内容={message.Content}, 时间={message.Timestamp:yyyy-MM-dd HH:mm:ss}");
            }
        }
    }
}
