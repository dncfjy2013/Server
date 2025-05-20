using Common.VaribelAttribute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace Utils.SystenDetect
{
    public partial class SystemDetector
    {
        // 网络带宽检测函数，返回网络带宽级别
        public static NetworkBandwidthProfile DetectNetworkBandwidthProfile()
        {
            try
            {
                // 获取所有可用的网络接口
                NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

                if (interfaces.Length == 0)
                {
                    Console.WriteLine("未检测到网络接口，网络带宽级别设置为Unknown");
                    return NetworkBandwidthProfile.Unknown;
                }

                // 寻找速度最快的活动网络接口
                long maxSpeed = 0;
                NetworkInterface activeInterface = null;

                foreach (NetworkInterface nic in interfaces)
                {
                    // 仅考虑活动的网络接口
                    if (nic.OperationalStatus == OperationalStatus.Up)
                    {
                        // 忽略回环接口
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                            continue;

                        // 获取接口速度（单位：bps）
                        long speed = nic.Speed;

                        // 速度为负值表示无法获取有效速度信息
                        if (speed > 0 && speed > maxSpeed)
                        {
                            maxSpeed = speed;
                            activeInterface = nic;
                        }
                    }
                }

                if (activeInterface == null)
                {
                    Console.WriteLine("未检测到活动的网络接口，网络带宽级别设置为Unknown");
                    return NetworkBandwidthProfile.Unknown;
                }

                // 输出检测到的网络接口信息
                Console.WriteLine($"检测到网络接口: {activeInterface.Name}, 类型: {activeInterface.NetworkInterfaceType}, 速度: {maxSpeed / 1000000} Mbps");

                // 将速度转换为Mbps进行判断
                double speedInMbps = maxSpeed / 1000000.0;

                // 根据带宽速度判断网络带宽级别
                if (speedInMbps >= 1000)
                    return NetworkBandwidthProfile.VeryHigh;
                if (speedInMbps >= 100)
                    return NetworkBandwidthProfile.High;
                if (speedInMbps >= 10)
                    return NetworkBandwidthProfile.Moderate;
                if (speedInMbps >= 1)
                    return NetworkBandwidthProfile.Low;

                return NetworkBandwidthProfile.VeryLow;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检测网络带宽时发生错误: {ex.Message}");
                return NetworkBandwidthProfile.Unknown;
            }
        }
    }
}
public enum NetworkBandwidthProfile
{
    [HardwareInfo("未知带宽", "无法确定网络带宽", "典型速度:<1 Mbps")]
    Unknown,

    [HardwareInfo("极低带宽", "带宽极低，如拨号网络", "典型速度:<1 Mbps", "延迟:高", "应用场景:简单文本浏览")]
    VeryLow,

    [HardwareInfo("低带宽", "低带宽，如低速移动网络", "典型速度:1-10 Mbps", "延迟:中等", "应用场景:标清视频、聊天")]
    Low,

    [HardwareInfo("中等带宽", "中等带宽，如有线宽带或4G", "典型速度:10-100 Mbps", "延迟:低", "应用场景:高清视频、在线游戏")]
    Moderate,

    [HardwareInfo("高带宽", "高带宽，如光纤或5G", "典型速度:100-1000 Mbps", "延迟:极低", "应用场景:4K视频、云游戏")]
    High,

    [HardwareInfo("超高速带宽", "超高速带宽，如企业级连接", "典型速度:>1000 Mbps", "延迟:极微", "应用场景:数据中心、大规模云服务")]
    VeryHigh
}