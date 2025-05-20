using Common.VaribelAttribute;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Utils.SystenDetect
{
    public partial class SystemDetector
    {
        public static DiskType DetectDiskType()
        {
            try
            {
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string testFile = Path.Combine(baseDirectory, "disk_test.tmp");

                // 创建临时文件并写入数据
                using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.ReadWrite,
                    FileShare.None, 4096, FileOptions.DeleteOnClose))
                {
                    var random = new Random();
                    byte[] buffer = new byte[4096];
                    random.NextBytes(buffer);

                    Stopwatch sw = Stopwatch.StartNew();
                    for (int i = 0; i < 100; i++)
                    {
                        long pos = random.Next(0, 1024 * 1024);
                        fs.Seek(pos, SeekOrigin.Begin);
                        fs.Write(buffer, 0, buffer.Length);
                    }
                    sw.Stop();

                    double avgMsPerWrite = sw.Elapsed.TotalMilliseconds / 100;

                    if (avgMsPerWrite > 5) return DiskType.HDD;
                    if (avgMsPerWrite > 0.1) return DiskType.SSD;
                    if (avgMsPerWrite < 0.1) return DiskType.NVMe;
                }

                // 重新打开文件进行内存映射测试
                using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None, 4096, FileOptions.DeleteOnClose))
                {
                    try
                    {
                        // 使用 .NET 8 正确的 API 重载
                        using var mmf = MemoryMappedFile.CreateFromFile(
                            fs,
                            null,
                            0,
                            MemoryMappedFileAccess.ReadWrite,
                            HandleInheritability.None,
                            false);

                        // 尝试创建视图访问器
                        using var accessor = mmf.CreateViewAccessor();
                        return DiskType.NVDIMM;
                    }
                    catch { }
                }

                return DiskType.Unknown;
            }
            catch { return DiskType.Unknown; }
        }
    }
}
public enum DiskType
{
    [HardwareInfo("机械硬盘", "传统旋转磁盘存储设备",
        "连续读写速度:100-200 MB/s", "随机4K读写速度:0.5-2 MB/s", "寻道时间:8-12 ms", "成本:低", "容量:大")]
    HDD,

    [HardwareInfo("固态硬盘(SATA)", "基于闪存的存储设备，SATA接口",
        "连续读写速度:500-600 MB/s", "随机4K读写速度:20-50 MB/s", "寻道时间:<0.1 ms", "成本:中等", "容量:中等")]
    SSD,

    [HardwareInfo("NVMe SSD", "基于NVMe协议的高性能SSD，PCIe接口",
        "连续读写速度:3000-7000 MB/s", "随机4K读写速度:200-500 MB/s", "寻道时间:<0.05 ms", "成本:高", "容量:中等")]
    NVMe,

    [HardwareInfo("非易失性内存", "高性能非易失性存储设备",
        "连续读写速度:10000-30000 MB/s", "随机4K读写速度:>1000 MB/s", "寻道时间:接近DRAM", "成本:极高", "容量:小")]
    NVDIMM,

    [HardwareInfo("未知类型", "无法识别的磁盘类型", "性能指标:N/A")]
    Unknown
}
