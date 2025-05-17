using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Server.Logger;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace loggertest
{
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [SimpleJob(RuntimeMoniker.Net80, launchCount: 1, warmupCount: 3, iterationCount: 5)]
    public class LoggerBenchmark
    {
        private ILogger _logger;
        private byte[] _smallMessage;
        private byte[] _mediumMessage;
        private byte[] _largeMessage;
        private readonly ConcurrentQueue<string> _logQueue = new();
        private readonly int _threadCount = Environment.ProcessorCount;
        private readonly int _iterationsPerThread = 1_000_000;
        private readonly List<LogLevel> _mixedLogLevels = new()
        {
            LogLevel.Trace, LogLevel.Debug, LogLevel.Information,
            LogLevel.Warning, LogLevel.Error, LogLevel.Critical
        };

        [GlobalSetup]
        public void Setup()
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            _logger = LoggerInstance.Instance;
            _logger.AddTemplate(new LogTemplate
            {
                Name = "BenchmarkTemplate",
                Template = "{Timestamp} [{Level}] {Message}",
                Level = LogLevel.Trace,
                IncludeException = false
            });

            _smallMessage = Encoding.UTF8.GetBytes(new string('A', 100));
            _mediumMessage = Encoding.UTF8.GetBytes(new string('B', 1000));
            _largeMessage = Encoding.UTF8.GetBytes(new string('C', 10000));

            Warmup();
        }

        private void Warmup()
        {
            Console.WriteLine("预热测试中...");
            Parallel.For(0, _threadCount, i =>
            {
                for (int j = 0; j < 1000; j++)
                {
                    _logger.LogInformation(new ReadOnlyMemory<byte>(_smallMessage));
                }
            });
            Thread.Sleep(2000);
            Console.WriteLine("预热完成");
        }

        [Benchmark(Description = "单线程-小消息")]
        public void SingleThread_SmallMessages()
        {
            for (int i = 0; i < _iterationsPerThread; i++)
            {
                _logger.LogInformation(new ReadOnlyMemory<byte>(_smallMessage));
            }
        }

        [Benchmark(Description = "单线程-混合大小消息")]
        public void SingleThread_MixedMessages()
        {
            for (int i = 0; i < _iterationsPerThread; i++)
            {
                var message = i % 3 == 0 ? _smallMessage :
                             i % 3 == 1 ? _mediumMessage : _largeMessage;

                _logger.Log(
                    _mixedLogLevels[i % _mixedLogLevels.Count],
                    message: new ReadOnlyMemory<byte>(message),
                    exception: null,
                    properties: null,
                    templateName: "BenchmarkTemplate"
                );
            }
        }

        [Benchmark(Description = "多线程-小消息")]
        public void MultiThread_SmallMessages()
        {
            Parallel.For(0, _threadCount, i =>
            {
                for (int j = 0; j < _iterationsPerThread / _threadCount; j++)
                {
                    _logger.LogInformation(new ReadOnlyMemory<byte>(_smallMessage));
                }
            });
        }

        [Benchmark(Description = "多线程-混合消息和级别")]
        public void MultiThread_Mixed()
        {
            Parallel.For(0, _threadCount, i =>
            {
                var random = new Random(i);
                for (int j = 0; j < _iterationsPerThread / _threadCount; j++)
                {
                    var messageSize = random.Next(3);
                    var messageBytes = messageSize == 0 ? _smallMessage :
                                      messageSize == 1 ? _mediumMessage : _largeMessage;

                    var level = _mixedLogLevels[random.Next(_mixedLogLevels.Count)];

                    _logger.Log(
                        level,
                        message: new ReadOnlyMemory<byte>(messageBytes),
                        exception: null,
                        properties: null,
                        templateName: "BenchmarkTemplate"
                    );
                }
            });
        }

        [Benchmark(Description = "极限并发-小消息")]
        public void MaxConcurrency_SmallMessages()
        {
            var tasks = new List<Task>();
            for (int i = 0; i < _threadCount * 4; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int j = 0; j < _iterationsPerThread / (_threadCount * 4); j++)
                    {
                        _logger.LogInformation(new ReadOnlyMemory<byte>(_smallMessage));
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
        }

        [Benchmark(Description = "异常日志测试")]
        public void ExceptionLogging()
        {
            Parallel.For(0, _threadCount, i =>
            {
                for (int j = 0; j < _iterationsPerThread / _threadCount; j++)
                {
                    try
                    {
                        if (j % 10 == 0) throw new InvalidOperationException("Test exception");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            new ReadOnlyMemory<byte>(_smallMessage),
                            ex,
                            templateName: "BenchmarkTemplate"
                        );
                    }
                }
            });
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            Console.WriteLine("测试完成，清理资源...");
            _logger.Dispose();
        }
    }
}