// See https://aka.ms/new-console-template for more information
using BenchmarkDotNet.Running;
using loggertest;

Console.WriteLine("Hello, World!");

var summary = BenchmarkRunner.Run<LoggerBenchmark>();

// 可选：输出结果到文件
Console.WriteLine("基准测试已完成，结果已保存到文件。");
