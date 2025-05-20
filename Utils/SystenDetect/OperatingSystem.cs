using Common.VaribelAttribute;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Utils.SystenDetect
{
    public partial class SystemDetector
    {
        // 操作系统检测
        [UnsupportedOSPlatform("browser")]
        public static OperatingSystemType DetectSystemType()
        {
            try
            {
                // 使用 .NET 8 增强的 OS 检测 API
                if (OperatingSystem.IsWindows())
                    return OperatingSystemType.Windows;

                if (OperatingSystem.IsLinux())
                {
                    if (File.Exists("/usr/bin/freebsd-version"))
                        return OperatingSystemType.FreeBSD;

                    if (File.Exists("/sbin/uname") && ExecuteCommand("/sbin/uname").Contains("SunOS"))
                        return OperatingSystemType.Solaris;

                    if (File.Exists("/usr/bin/openbsd-version"))
                        return OperatingSystemType.OpenBSD;

                    if (File.Exists("/usr/bin/netbsd-version"))
                        return OperatingSystemType.NetBSD;

                    return OperatingSystemType.Linux;
                }

                if (OperatingSystem.IsMacOS())
                    return OperatingSystemType.macOS;

                // 回退到传统检测方法
                return DetectSystemTypeFallback();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"操作系统检测错误: {ex.Message}");
                return OperatingSystemType.Unknown;
            }
        }

        // 传统操作系统检测方法
        private static OperatingSystemType DetectSystemTypeFallback()
        {
            string osName = Environment.OSVersion.Platform.ToString().ToLower();

            if (osName.Contains("unix") || osName.Contains("linux"))
            {
                if (File.Exists("/etc/os-release"))
                {
                    string osRelease = File.ReadAllText("/etc/os-release").ToLower();
                    if (osRelease.Contains("freebsd"))
                        return OperatingSystemType.FreeBSD;
                    if (osRelease.Contains("solaris"))
                        return OperatingSystemType.Solaris;
                    if (osRelease.Contains("openbsd"))
                        return OperatingSystemType.OpenBSD;
                    if (osRelease.Contains("netbsd"))
                        return OperatingSystemType.NetBSD;

                    return OperatingSystemType.Linux;
                }

                if (Directory.Exists("/System/Library"))
                    return OperatingSystemType.macOS;
            }

            if (File.Exists("/proc/version"))
            {
                string procVersion = File.ReadAllText("/proc/version").ToLower();
                if (procVersion.Contains("freebsd"))
                    return OperatingSystemType.FreeBSD;
                if (procVersion.Contains("solaris"))
                    return OperatingSystemType.Solaris;
                if (procVersion.Contains("openbsd"))
                    return OperatingSystemType.OpenBSD;
                if (procVersion.Contains("netbsd"))
                    return OperatingSystemType.NetBSD;
                if (procVersion.Contains("linux"))
                    return OperatingSystemType.Linux;
            }

            return OperatingSystemType.Unknown;
        }

        // 使用 .NET 8 改进的进程 API
        private static string ExecuteCommand(string command, string arguments = "")
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }

        // 异步版本的命令执行（可用于非阻塞操作）
        [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Process execution may require types that cannot be statically analyzed.")]
        private static async Task<string> ExecuteCommandAsync(string command, string arguments = "")
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                return output;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
public enum OperatingSystemType
{
    [HardwareInfo("Windows", "微软Windows操作系统", "市场份额:高", "桌面应用支持:优秀", "游戏支持:优秀", "用户群体:广泛")]
    Windows,

    [HardwareInfo("Linux", "开源Linux操作系统家族", "市场份额:中等(桌面)，高(服务器)", "桌面应用支持:中等", "服务器应用支持:优秀", "用户群体:开发者、服务器管理员")]
    Linux,

    [HardwareInfo("macOS", "苹果Macintosh操作系统", "市场份额:低", "桌面应用支持:优秀", "设计软件支持:优秀", "用户群体:创意工作者、苹果用户")]
    macOS,

    [HardwareInfo("FreeBSD", "开源类Unix操作系统", "市场份额:低", "服务器应用支持:优秀", "网络功能:强大", "用户群体:高级系统管理员")]
    FreeBSD,

    [HardwareInfo("Solaris", "甲骨文UNIX操作系统", "市场份额:极低", "企业级应用支持:优秀", "可靠性:极高", "用户群体:大型企业、数据中心")]
    Solaris,

    [HardwareInfo("OpenBSD", "开源类Unix操作系统", "市场份额:极低", "安全性:极高", "密码学:优秀", "用户群体:安全专家、研究人员")]
    OpenBSD,

    [HardwareInfo("NetBSD", "开源类Unix操作系统", "市场份额:极低", "平台支持:广泛", "嵌入式系统:优秀", "用户群体:开发者、嵌入式系统工程师")]
    NetBSD,

    [HardwareInfo("未知系统", "无法识别的操作系统", "特性:N/A")]
    Unknown
}