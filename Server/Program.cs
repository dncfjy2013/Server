using Server;
using Server.Core.Certification;
using Common.VaribelAttribute;
using Server.DataBase.Core.RelateSQL;

var hardwareInfo = HardwareDetector.DetectHardwareInfo();
Console.WriteLine($"CPU: {hardwareInfo.CpuInfo}");
Console.WriteLine($"核心数: {hardwareInfo.CpuCores}");
Console.WriteLine($"内存: {hardwareInfo.TotalMemoryGb} GB");
Console.WriteLine($"磁盘类型: {hardwareInfo.DiskType}");
Console.WriteLine($"系统类型: {hardwareInfo.System}");

Server.Core.ServerInstance server = new Server.Core.ServerInstance(1111, 2222, 3333, new List<string>() { "http://localhost:9999/" }, SSLManager.LoadOrCreateCertificate());
server.Start(false);

Console.WriteLine("press enter to stop the server...");
Console.ReadLine();

server.Stop();