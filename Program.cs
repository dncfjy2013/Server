using BenchmarkDotNet.Running;
using loggertest;
using Server.Core.Certification;
using Server.Proxy.Common;
using Server.Test;

//Server.Core.ServerInstance server = new Server.Core.ServerInstance(1111, 2222, 3333, "http://localhost:9999/", SSLManager.LoadOrCreateCertificate());
//server.Start(false);

////console.writeline("press enter to stop the server...");
//Console.ReadLine();

//server.Stop();
//IP_ZONE.Main();

// 运行所有基准测试
var summary = BenchmarkRunner.Run<LoggerBenchmark>();

// 可选：输出结果到文件
Console.WriteLine("基准测试已完成，结果已保存到文件。");