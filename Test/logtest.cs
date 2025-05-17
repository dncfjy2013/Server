// 安装依赖：BenchmarkDotNet、Microsoft.Extensions.DependencyInjection
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Server.Logger;
using Server.Logger.Common;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace loggertest
{
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net80, launchCount: 1, warmupCount: 5, invocationCount: 10)]
    public class LoggerBenchmark
    {
        private ILogger _logger;
        private readonly string _testMessage = "This is a test log message";
        private readonly ConcurrentQueue<string> _logQueue = new();

        [GlobalSetup]
        public void Setup()
        {
            _logger = LoggerInstance.Instance;
            _logger.AddTemplate(new LogTemplate
            {
                Name = "BenchmarkTemplate",
                Template = "{Timestamp} [{Level}] {Message}",
                Level = LogLevel.Information,
                IncludeException = false
            });
        }

        [Benchmark(Baseline = true)]
        public async Task Log_Information_Sync()
        {
            for (int i = 0; i < 1_000_000; i++) // 单次测试100万条，可调整为千万/亿级
            {
                _logger.LogInformation(_testMessage, templateName: "BenchmarkTemplate");
            }
            await Task.Yield();
        }

        [Benchmark]
        public async Task Log_Information_Async()
        {
            for (int i = 0; i < 1_000_000; i++)
            {
                _logger.LogInformation(_testMessage, templateName: "BenchmarkTemplate");
            }
            await Task.Yield();
        }

        [Benchmark]
        public async Task Log_With_Properties()
        {
            var properties = new Dictionary<string, object> { { "TestKey", "TestValue" } };
            for (int i = 0; i < 1_000_000; i++)
            {
                _logger.LogInformation(_testMessage, properties, templateName: "BenchmarkTemplate");
            }
            await Task.Yield();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _logger.Dispose();
        }
    }
}