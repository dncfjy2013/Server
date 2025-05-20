using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Server.DataBase.Core.RelateSQL
{
    public enum DiskType
    {
        HDD,
        SSD,
        NVMe,
        Unknown
    }

    public class HardwareInfo
    {
        public string CpuInfo { get; set; } = "Unknown";
        public int CpuCores { get; set; } = 0;
        public double TotalMemoryGb { get; set; } = 0;
        public DiskType DiskType { get; set; } = DiskType.Unknown;
    }

    public static class HardwareDetector
    {
        public static HardwareInfo DetectHardwareInfo()
        {
            var info = new HardwareInfo();

            try
            {
                // 检测操作系统
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    DetectWindowsHardware(info);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    DetectLinuxHardware(info);
                }
                else
                {
                    info.CpuInfo = "Unsupported OS";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"硬件检测失败: {ex.Message}");
            }

            return info;
        }

        private static void DetectWindowsHardware(HardwareInfo info)
        {
            // ---------------------- CPU 信息 ----------------------
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            var processors = searcher.Get().Cast<System.Management.ManagementObject>().ToList();

            if (processors.Any())
            {
                var firstProc = processors[0];
                info.CpuInfo = firstProc["Name"]?.ToString() ?? "Unknown CPU";
                info.CpuCores = Convert.ToInt32(firstProc["NumberOfCores"]);
            }

            // ---------------------- 内存信息 ----------------------
            using var memSearcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
            var memInfo = memSearcher.Get().Cast<System.Management.ManagementObject>().FirstOrDefault();

            if (memInfo != null)
            {
                ulong totalMemoryBytes = Convert.ToUInt64(memInfo["TotalPhysicalMemory"]);
                info.TotalMemoryGb = Math.Round(totalMemoryBytes / (1024.0 * 1024.0 * 1024.0), 1);
            }

            // ---------------------- 磁盘类型 ----------------------
            using var diskSearcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (var disk in diskSearcher.Get().Cast<System.Management.ManagementObject>())
            {
                string model = disk["Model"]?.ToString() ?? "";
                if (model.IndexOf("SSD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    model.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    model.IndexOf("Solid State", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    info.DiskType = model.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0
                        ? DiskType.NVMe
                        : DiskType.SSD;
                    break;
                }
            }

            if (info.DiskType == DiskType.Unknown)
            {
                info.DiskType = DiskType.HDD; // 默认设为HDD
            }
        }

        private static void DetectLinuxHardware(HardwareInfo info)
        {
            // ---------------------- CPU 信息 ----------------------
            if (File.Exists("/proc/cpuinfo"))
            {
                var cpuInfo = File.ReadAllText("/proc/cpuinfo");
                var modelLine = Regex.Match(cpuInfo, @"model name\s+:\s+(.*)").Groups[1].Value.Trim();
                info.CpuInfo = modelLine.Length > 0 ? modelLine : "Unknown CPU";
                info.CpuCores = Environment.ProcessorCount;
            }

            // ---------------------- 内存信息 ----------------------
            if (File.Exists("/proc/meminfo"))
            {
                var memInfo = File.ReadAllText("/proc/meminfo");
                var memLine = Regex.Match(memInfo, @"MemTotal:\s+(\d+)\s+kB").Groups[1].Value;
                if (long.TryParse(memLine, out var totalKb))
                {
                    info.TotalMemoryGb = Math.Round(totalKb / (1024.0 * 1024), 1);
                }
            }

            // ---------------------- 磁盘类型 ----------------------
            var diskPath = "/sys/block/";
            if (Directory.Exists(diskPath))
            {
                foreach (var disk in Directory.GetDirectories(diskPath))
                {
                    var diskName = Path.GetFileName(disk);
                    if (diskName.StartsWith("nvme"))
                    {
                        info.DiskType = DiskType.NVMe;
                        break;
                    }
                    else if (File.Exists(Path.Combine(disk, "queue/rotational")))
                    {
                        var rotational = File.ReadAllText(Path.Combine(disk, "queue/rotational")).Trim();
                        if (rotational == "0") // 0表示SSD，1表示HDD
                        {
                            info.DiskType = DiskType.SSD;
                            break;
                        }
                    }
                }
            }

            if (info.DiskType == DiskType.Unknown)
            {
                info.DiskType = DiskType.HDD; // 默认设为HDD
            }
        }
    }
}