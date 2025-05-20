//// See https://aka.ms/new-console-template for more information
//using BenchmarkDotNet.Running;
//using Test.TestDoc;

//Console.WriteLine("Hello, World!");

//var summary = BenchmarkRunner.Run<LoggerBenchmark>();

//// 可选：输出结果到文件
//Console.WriteLine("基准测试已完成，结果已保存到文件。");

using Common.VaribelAttribute;
using Utils.SystenDetect;

// 获取并输出系统类型的详细信息
var systemType = SystemDetector.DetectSystemType();
Console.WriteLine($"系统类型: {systemType.GetHardwareInfo()?.FullName ?? systemType.ToString()}");
Console.WriteLine(systemType.GetFullDescription());

// 获取并输出硬件配置文件的详细信息
var hardwareProfile = SystemDetector.DetectHardwareProfile();
Console.WriteLine($"硬件配置: {hardwareProfile.GetHardwareInfo()?.FullName ?? hardwareProfile.ToString()}");
Console.WriteLine(hardwareProfile.GetFullDescription());

// 获取并输出网络带宽配置文件的详细信息
var networkProfile = SystemDetector.DetectNetworkBandwidthProfile();
Console.WriteLine($"网络带宽: {networkProfile.GetHardwareInfo()?.FullName ?? networkProfile.ToString()}");
Console.WriteLine(networkProfile.GetFullDescription());

// 获取并输出磁盘类型的详细信息
var diskType = SystemDetector.DetectDiskType();
Console.WriteLine($"磁盘类型: {diskType.GetHardwareInfo()?.FullName ?? diskType.ToString()}");
Console.WriteLine(diskType.GetFullDescription());

while (true)
{

}