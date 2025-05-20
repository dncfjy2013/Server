using Common.VaribelAttribute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utils.SystenDetect
{
    public partial class SystemDetector
    {
        public static HardwareProfile DetectHardwareProfile()
        {
            long totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            int processorCount = Environment.ProcessorCount;

            if (processorCount <= 4 || totalMemory < 8L * 1024 * 1024 * 1024)
                return HardwareProfile.Mobile;

            if (processorCount <= 16 || totalMemory < 64L * 1024 * 1024 * 1024)
                return HardwareProfile.Standard;

            if (processorCount <= 64 || totalMemory < 512L * 1024 * 1024 * 1024)
                return HardwareProfile.HighPerformance;

            return HardwareProfile.ExtremePerformance;
        }
    }
}
public enum HardwareProfile
{
    [HardwareInfo("移动设备", "适用于智能手机、平板等移动设备",
        "CPU性能:低-中等", "内存容量:2-8GB", "图形性能:集成显卡", "电池续航:高")]
    Mobile,

    [HardwareInfo("标准配置", "适用于普通办公和家庭使用的计算机",
        "CPU性能:中等", "内存容量:8-16GB", "图形性能:入门级独立显卡", "电池续航:中等")]
    Standard,

    [HardwareInfo("高性能配置", "适用于游戏、设计和多任务处理的计算机",
        "CPU性能:高", "内存容量:16-32GB", "图形性能:中高端独立显卡", "电池续航:低")]
    HighPerformance,

    [HardwareInfo("极致性能配置", "适用于专业工作站和高端游戏PC",
        "CPU性能:极高", "内存容量:32GB以上", "图形性能:顶级独立显卡", "电池续航:极低")]
    ExtremePerformance
}
