using Common.VaribelAttribute;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Utils
{
    public class HardWareDetect
    {

        // 检测并显示所有硬件信息
        public static void DisplayAllHardwareInfo()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("        系统硬件信息检测报告");
            Console.WriteLine("========================================");
            Console.WriteLine();

            DisplaySystemInfo();
            Console.WriteLine();

            DisplayCpuInfo();
            Console.WriteLine();

            DisplayMemoryInfo();
            Console.WriteLine();

            DisplayDiskInfo();
            Console.WriteLine();

            DisplayNetworkInfo();
            Console.WriteLine();

            DisplayGraphicsInfo();
            Console.WriteLine();

            Console.WriteLine("========================================");

        }

        // 显示系统信息
        private static void DisplaySystemInfo()
        {
            Console.WriteLine("【系统信息】");

            try
            {
                // 获取操作系统信息
                Console.WriteLine($"操作系统: {RuntimeInformation.OSDescription}");

                // 获取系统架构
                Console.WriteLine($"系统架构: {RuntimeInformation.ProcessArchitecture}");

                // 获取系统启动时间
                TimeSpan uptime = Process.GetCurrentProcess().StartTime - DateTime.MinValue;
                Console.WriteLine($"系统已运行时间: {uptime:dd\\.hh\\:mm\\:ss}");

                // 获取系统语言
                Console.WriteLine($"系统语言: {System.Globalization.CultureInfo.CurrentCulture.DisplayName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取系统信息时出错: {ex.Message}");
            }
        }

        // 显示CPU信息
        private static void DisplayCpuInfo()
        {
            Console.WriteLine("【CPU信息】");

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        Console.WriteLine($"CPU型号: {obj["Name"]}");
                        Console.WriteLine($"CPU厂商: {obj["Manufacturer"]}");
                        Console.WriteLine($"CPU核心数: {obj["NumberOfCores"]}");
                        Console.WriteLine($"CPU逻辑处理器数: {obj["NumberOfLogicalProcessors"]}");
                        Console.WriteLine($"CPU主频: {obj["MaxClockSpeed"]} MHz");
                        Console.WriteLine($"CPU状态: {obj["Status"]}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取CPU信息时出错: {ex.Message}");
            }
        }

        // 显示内存信息
        private static void DisplayMemoryInfo()
        {
            Console.WriteLine("【内存信息】");

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        ulong totalMemory = Convert.ToUInt64(obj["TotalPhysicalMemory"]) / (1024 * 1024 * 1024);
                        Console.WriteLine($"总内存: {totalMemory} GB");
                    }
                }

                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
                {
                    int memorySlot = 1;
                    foreach (var obj in searcher.Get())
                    {
                        ulong capacity = Convert.ToUInt64(obj["Capacity"]) / (1024 * 1024 * 1024);
                        Console.WriteLine($"内存插槽 {memorySlot}: {capacity} GB {obj["Speed"]} MHz {obj["MemoryType"]}");
                        memorySlot++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取内存信息时出错: {ex.Message}");
            }
        }

        // 显示磁盘信息
        private static void DisplayDiskInfo()
        {
            Console.WriteLine("【磁盘信息】");

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
                {
                    int diskIndex = 1;
                    foreach (var drive in searcher.Get())
                    {
                        Console.WriteLine($"磁盘 {diskIndex}: {drive["Model"]}");
                        Console.WriteLine($"  容量: {BytesToHumanReadable(Convert.ToUInt64(drive["Size"]))}");
                        Console.WriteLine($"  接口类型: {drive["InterfaceType"]}");

                        // 尝试确定磁盘类型
                        string driveModel = drive["Model"].ToString().ToLower();
                        DiskType diskType = DiskType.Unknown;

                        if (driveModel.Contains("nvme") || driveModel.Contains("pcie"))
                            diskType = DiskType.NVMe;
                        else if (driveModel.Contains("ssd"))
                            diskType = DiskType.SSD;
                        else if (driveModel.Contains("hdd") || driveModel.Contains("disk"))
                            diskType = DiskType.HDD;

                        Console.WriteLine($"  磁盘类型: {diskType.GetHardwareInfo()?.FullName ?? diskType.ToString()}");

                        // 获取分区信息
                        string driveIndex = drive["Index"].ToString();
                        using (var partitionSearcher = new ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{drive["DeviceID"]}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                        {
                            foreach (var partition in partitionSearcher.Get())
                            {
                                using (var logicalDriveSearcher = new ManagementObjectSearcher(
                                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partition["DeviceID"]}'}} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                                {
                                    foreach (var logicalDrive in logicalDriveSearcher.Get())
                                    {
                                        Console.WriteLine($"  分区: {logicalDrive["Name"]} - {logicalDrive["VolumeName"]}");
                                        if (logicalDrive["Size"] != null && logicalDrive["FreeSpace"] != null)
                                        {
                                            ulong size = Convert.ToUInt64(logicalDrive["Size"]);
                                            ulong freeSpace = Convert.ToUInt64(logicalDrive["FreeSpace"]);
                                            double freePercent = (double)freeSpace / size * 100;

                                            Console.WriteLine($"    容量: {BytesToHumanReadable(size)}, 可用: {BytesToHumanReadable(freeSpace)} ({freePercent:F2}%)");
                                        }
                                    }
                                }
                            }
                        }

                        diskIndex++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取磁盘信息时出错: {ex.Message}");
            }
        }

        // 显示网络信息
        private static void DisplayNetworkInfo()
        {
            Console.WriteLine("【网络信息】");

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up)
                    {
                        Console.WriteLine($"网络适配器: {nic.Name} ({nic.Description})");
                        Console.WriteLine($"  类型: {nic.NetworkInterfaceType}");
                        Console.WriteLine($"  状态: {nic.OperationalStatus}");
                        Console.WriteLine($"  速度: {nic.Speed / (1000 * 1000)} Mbps");
                        Console.WriteLine($"  MAC地址: {nic.GetPhysicalAddress()}");

                        // 获取IP地址
                        var ipProperties = nic.GetIPProperties();
                        Console.WriteLine("  IP地址:");

                        foreach (var unicastIP in ipProperties.UnicastAddresses)
                        {
                            Console.WriteLine($"    {unicastIP.Address} ({unicastIP.AddressPreferredLifetime})");
                        }

                        // 检测网络带宽配置文件
                        NetworkBandwidthProfile bandwidthProfile = DetectNetworkBandwidthProfile(nic);
                        Console.WriteLine($"  带宽配置: {bandwidthProfile.GetHardwareInfo()?.FullName ?? bandwidthProfile.ToString()}");

                        Console.WriteLine();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取网络信息时出错: {ex.Message}");
            }
        }

        // 显示显卡信息
        private static void DisplayGraphicsInfo()
        {
            Console.WriteLine("【显卡信息】");

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        Console.WriteLine($"显卡: {obj["Name"]}");
                        Console.WriteLine($"  厂商: {obj["AdapterCompatibility"]}");

                        if (obj["AdapterRAM"] != null)
                        {
                            ulong ram = Convert.ToUInt64(obj["AdapterRAM"]) / (1024 * 1024);
                            Console.WriteLine($"  显存: {ram} MB");
                        }

                        Console.WriteLine($"  驱动版本: {obj["DriverVersion"]}");
                        Console.WriteLine($"  分辨率: {obj["CurrentHorizontalResolution"]} x {obj["CurrentVerticalResolution"]}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取显卡信息时出错: {ex.Message}");
            }
        }

        // 辅助方法：将字节转换为人类可读的格式
        private static string BytesToHumanReadable(ulong bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {suffixes[order]}";
        }

        // 检测网络带宽配置文件
        private static NetworkBandwidthProfile DetectNetworkBandwidthProfile(NetworkInterface nic)
        {
            long speed = nic.Speed;

            if (speed < 1000000)  // < 1 Mbps
                return NetworkBandwidthProfile.VeryLow;
            if (speed < 10000000)  // < 10 Mbps
                return NetworkBandwidthProfile.Low;
            if (speed < 100000000)  // < 100 Mbps
                return NetworkBandwidthProfile.Moderate;
            if (speed < 1000000000)  // < 1 Gbps
                return NetworkBandwidthProfile.High;

            return NetworkBandwidthProfile.VeryHigh;
        }
    }
}
