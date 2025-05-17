using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Logger
{
    public class AsyncConsoleLogger : IDisposable
    {
        private const int BatchSize = 1000; // 批量处理大小
        private const int MaxBufferSize = 10_000; // 每个线程本地缓冲区最大容量
        private const int FlushIntervalMs = 500; // 强制刷新间隔（毫秒）

        private readonly ConcurrentQueue<List<string>> _batches = new();
        private readonly SemaphoreSlim _semaphore = new(0);
        private readonly Thread _consumerThread;
        private readonly Timer _flushTimer;
        private bool _isRunning = true;

        public AsyncConsoleLogger()
        {
            _consumerThread = new Thread(ConsumeBatches)
            {
                IsBackground = true,
                Priority = ThreadPriority.Lowest
            };
            _consumerThread.Start();

            // 定期强制刷新所有线程缓冲区
            _flushTimer = new Timer(_ => FlushAllBuffers(), null, FlushIntervalMs, FlushIntervalMs);
        }

        // 线程本地缓冲区 - 减少跨线程锁竞争
        private static readonly ThreadLocal<List<string>> _threadBuffer =
            new(() => new List<string>(BatchSize));

        public void EnqueueLog(string logLine)
        {
            if (!_isRunning) return;

            var buffer = _threadBuffer.Value;
            lock (buffer)
            {
                buffer.Add(logLine);
                if (buffer.Count >= BatchSize)
                {
                    FlushThreadBuffer(buffer);
                    buffer.Clear();
                }
            }
        }

        private void FlushThreadBuffer(List<string> buffer)
        {
            if (buffer.Count == 0) return;

            // 创建新列表并转移引用，减少锁持有时间
            var batch = new List<string>(buffer);
            _batches.Enqueue(batch);
            _semaphore.Release();
        }

        private void FlushAllBuffers()
        {
            try
            {
                // 强制刷新所有线程的缓冲区（利用静态字段反射）
                foreach (var field in typeof(ThreadLocal<List<string>>)
                    .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                {
                    var value = field.GetValue(_threadBuffer) as List<string>;
                    if (value != null && value.Count > 0)
                    {
                        lock (value)
                        {
                            FlushThreadBuffer(value);
                            value.Clear();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error flushing buffers: {ex.Message}");
            }
        }

        private void ConsumeBatches()
        {
            while (_isRunning || !_batches.IsEmpty)
            {
                try
                {
                    _semaphore.Wait(100); // 避免CPU空转

                    var processed = 0;
                    while (processed < 10 && _batches.TryDequeue(out var batch))
                    {
                        processed++;
                        WriteBatchToConsole(batch);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Batch processing error: {ex.Message}");
                }
            }
        }

        private void WriteBatchToConsole(List<string> batch)
        {
            try
            {
                // 使用 Console.Out 的底层 StreamWriter 进行批量写入
                var writer = Console.Out;
                lock (writer)
                {
                    foreach (var line in batch)
                    {
                        writer.WriteLine(line);
                    }
                    writer.Flush(); // 确保数据立即输出
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Write error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _isRunning = false;
            _flushTimer.Dispose();
            _semaphore.Release(); // 唤醒消费者线程
            _consumerThread.Join(2000);

            // 清空剩余缓冲区
            FlushAllBuffers();
            while (_batches.TryDequeue(out var batch))
            {
                WriteBatchToConsole(batch);
            }
        }
    }
}
