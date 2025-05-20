using Server;
using Server.Core.Certification;
using Utils.SystenDetect;
using Common.VaribelAttribute;

InfoDetect.DisplayAllHardwareInfo();

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

Server.Core.ServerInstance server = new Server.Core.ServerInstance(1111, 2222, 3333, "http://localhost:9999/", SSLManager.LoadOrCreateCertificate());
server.Start(false);

Console.WriteLine("press enter to stop the server...");
Console.ReadLine();

server.Stop();