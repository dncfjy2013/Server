using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Entity.IPCCommunication.MesageQueue
{
    // 共享内存消息队列（支持多生产者多消费者）
    public sealed class SharedMemoryMessageQueue<T> : IDisposable where T : class
    {
        private const string QueueNamePrefix = "Global\\IPC_Queue_";
        private readonly string _queueName;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewStream _stream;
        private readonly Channel<T> _channel;
        private readonly Task _readerTask;
        private bool _disposed;

        public SharedMemoryMessageQueue(string queueName, bool create = false)
        {
            _queueName = $"{QueueNamePrefix}{queueName}";

            if (create)
            {
                // 创建共享内存（1GB 容量，可根据需求调整）
                _mmf = MemoryMappedFile.CreateOrOpen(_queueName, 1024 * 1024 * 1024);
                _stream = _mmf.CreateViewStream();
                _channel = Channel.CreateUnbounded<T>();

                // 启动读取任务（从共享内存读取消息到 Channel）
                _readerTask = Task.Run(ReadMessagesAsync);
            }
            else
            {
                // 打开现有共享内存
                _mmf = MemoryMappedFile.OpenExisting(_queueName);
                _stream = _mmf.CreateViewStream();
                _channel = Channel.CreateUnbounded<T>();
            }
        }

        // 发送消息（生产者）
        public async Task SendAsync(T message)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SharedMemoryMessageQueue<T>));

            // 写入消息到共享内存
            await _stream.WriteAsync(BitConverter.GetBytes(-1)); // 消息开始标记

            var json = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(json);

            await _stream.WriteAsync(BitConverter.GetBytes(messageBytes.Length));
            await _stream.WriteAsync(messageBytes);

            await _stream.WriteAsync(BitConverter.GetBytes(-2)); // 消息结束标记
            await _stream.FlushAsync();
        }

        // 接收消息（消费者）
        public IAsyncEnumerable<T> ReceiveAllAsync(CancellationToken cancellationToken = default)
            => _channel.Reader.ReadAllAsync(cancellationToken);

        // 从共享内存读取消息到 Channel
        private async Task ReadMessagesAsync()
        {
            try
            {
                while (!_disposed)
                {
                    // 检测消息开始标记
                    var startMarker = await ReadInt32Async();
                    if (startMarker != -1) continue;

                    // 读取消息长度
                    var length = await ReadInt32Async();
                    if (length <= 0) continue;

                    // 读取消息内容
                    var buffer = new byte[length];
                    await _stream.ReadAsync(buffer, 0, length);

                    // 检测消息结束标记
                    var endMarker = await ReadInt32Async();
                    if (endMarker != -2) continue;

                    // 反序列化并发布到 Channel
                    var json = Encoding.UTF8.GetString(buffer);
                    var message = JsonSerializer.Deserialize<T>(json);
                    await _channel.Writer.WriteAsync(message);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常关闭
            }
            catch (Exception ex)
            {
                Console.WriteLine($"消息读取错误: {ex.Message}");
            }
        }

        private async Task<int> ReadInt32Async()
        {
            var buffer = new byte[4];
            await _stream.ReadAsync(buffer, 0, 4);
            return BitConverter.ToInt32(buffer, 0);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _channel.Writer.Complete();
            _readerTask?.Wait(1000);

            _stream?.Dispose();
            _mmf?.Dispose();
        }
    }

    public class MessageQueueTest
    {
        // 定义可序列化的消息类型
        public class MyMessage
        {
            public int Id { get; set; }
            public string Content { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public async Task mainAsync()
        {
            // 进程A（发送者）
            using var queueA = new SharedMemoryMessageQueue<MyMessage>("MyQueue", create: true);

            for (int i = 0; i < 10; i++)
            {
                await queueA.SendAsync(new MyMessage
                {
                    Id = i,
                    Content = $"消息 {i}",
                    Timestamp = DateTime.Now
                });

                Console.WriteLine($"已发送: 消息 {i}");
                await Task.Delay(500);
            }

            // 进程B（接收者）
            using var queueB = new SharedMemoryMessageQueue<MyMessage>("MyQueue");

            await foreach (var message in queueB.ReceiveAllAsync())
            {
                Console.WriteLine($"收到消息: ID={message.Id}, 内容={message.Content}, 时间={message.Timestamp}");
            }
        }
    }
}
