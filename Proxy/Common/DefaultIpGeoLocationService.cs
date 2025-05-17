using Server.Logger;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Server.Proxy.Common
{
    /// <summary>
    /// 高性能 IP 地理位置服务，支持 IP→区域 和 区域→IP段 双向映射，支持 IPv4 和 IPv6
    /// </summary>
    public sealed class DefaultIpGeoLocationService : IDisposable
    {
        #region 内部数据结构

        /// <summary>
        /// IP段信息（不可变结构体）
        /// </summary>
        private readonly struct IpRange : IComparable<IpRange>
        {
            public readonly IPAddress Network;
            public readonly int PrefixLength;
            public readonly string Zone;

            public IpRange(IPAddress network, int prefixLength, string zone)
            {
                Network = network;
                PrefixLength = prefixLength;
                Zone = zone ?? "unknown";
            }

            /// <summary>
            /// 按前缀长度降序排序（用于优先匹配更精确的网段）
            /// </summary>
            public int CompareTo(IpRange other) => other.PrefixLength.CompareTo(PrefixLength);

            /// <summary>
            /// 检查IP是否属于当前网段
            /// </summary>
            public bool Contains(IPAddress ip)
            {
                if (Network.AddressFamily != ip.AddressFamily)
                    return false;

                if (Network.AddressFamily == AddressFamily.InterNetwork)
                {
                    // IPv4 比较
                    var networkBytes = Network.GetAddressBytes();
                    var ipBytes = ip.GetAddressBytes();
                    var maskBytes = GetMaskBytes(32, PrefixLength);

                    for (int i = 0; i < 4; i++)
                    {
                        if ((networkBytes[i] & maskBytes[i]) != (ipBytes[i] & maskBytes[i]))
                            return false;
                    }
                    return true;
                }
                else
                {
                    // IPv6 比较
                    var networkBytes = Network.GetAddressBytes();
                    var ipBytes = ip.GetAddressBytes();
                    var maskBytes = GetMaskBytes(128, PrefixLength);

                    for (int i = 0; i < 16; i++)
                    {
                        if ((networkBytes[i] & maskBytes[i]) != (ipBytes[i] & maskBytes[i]))
                            return false;
                    }
                    return true;
                }
            }

            private static byte[] GetMaskBytes(int addressBits, int prefixLength)
            {
                var maskBytes = new byte[addressBits / 8];
                int fullBytes = prefixLength / 8;
                int remainingBits = prefixLength % 8;

                for (int i = 0; i < fullBytes; i++)
                    maskBytes[i] = 0xFF;

                if (remainingBits > 0)
                    maskBytes[fullBytes] = (byte)(0xFF << (8 - remainingBits));

                return maskBytes;
            }

            public override string ToString()
            {
                return $"{Network}/{PrefixLength}";
            }
        }

        #endregion

        #region 成员变量

        private readonly Options _options;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, CacheEntry> _ipZoneCache;
        private readonly List<IpRange> _ipv4Ranges;
        private readonly List<IpRange> _ipv6Ranges;
        private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _zoneToCidrs;
        private int _disposed;

        #endregion

        #region 构造函数

        /// <summary>
        /// 使用默认配置初始化服务
        /// </summary>
        public DefaultIpGeoLocationService(ILogger logger)
            : this(new Options(), logger)
        {
        }

        /// <summary>
        /// 使用自定义配置初始化服务
        /// </summary>
        public DefaultIpGeoLocationService(Options options, ILogger logger)
        {
            ValidateOptions(options);
            _options = options;
            _logger = logger;
            _ipZoneCache = new ConcurrentDictionary<string, CacheEntry>(
                StringComparer.OrdinalIgnoreCase);
            _ipv4Ranges = new List<IpRange>();
            _ipv6Ranges = new List<IpRange>();
            _zoneToCidrs = new ConcurrentDictionary<string, ConcurrentBag<string>>(
                StringComparer.OrdinalIgnoreCase);

            LoadDefaultMappings();
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 从配置文件加载IP段映射规则（支持CIDR格式，IPv4和IPv6）
        /// </summary>
        public void LoadFromConfig(string filePath)
        {
            CheckDisposed();

            if (!File.Exists(filePath))
            {
                _logger.LogWarning($"Configuration file not found: {filePath}");
                return;
            }

            try
            {
                using var reader = new StreamReader(filePath);
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine()?.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

                    var parts = line.Split(new[] { ' ', '\t', ',' }, 2, StringSplitOptions.TrimEntries);
                    if (parts.Length != 2)
                    {
                        _logger.LogWarning($"Invalid line format (requires CIDR and zone): {line}");
                        continue;
                    }

                    if (TryAddMapping(parts[0], parts[1]))
                    {
                        _logger.LogDebug($"Successfully loaded rule: {parts[0]} → {parts[1]}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// 添加自定义IP段映射规则（支持IPv4和IPv6）
        /// </summary>
        public bool TryAddMapping(string cidr, string zone)
        {
            CheckDisposed();

            if (string.IsNullOrEmpty(cidr) || string.IsNullOrEmpty(zone))
            {
                _logger.LogWarning("CIDR or zone is empty");
                return false;
            }

            if (!TryParseCidr(cidr, out var network, out var prefixLength))
            {
                _logger.LogWarning($"Invalid CIDR format: {cidr}");
                return false;
            }

            AddMappingInternal(network, prefixLength, zone, cidr);
            return true;
        }

        /// <summary>
        /// 根据IP地址获取区域（支持IPv4和IPv6）
        /// </summary>
        public string GetZoneByIp(string ipAddress)
        {
            CheckDisposed();

            if (string.IsNullOrEmpty(ipAddress)) return "unknown";

            // 检查缓存（含过期时间）
            if (_ipZoneCache.TryGetValue(ipAddress, out var entry) &&
                entry.Expiration > DateTime.UtcNow)
            {
                return entry.Zone;
            }

            // 解析IP地址
            if (!IPAddress.TryParse(ipAddress, out var ip))
            {
                _logger.LogWarning($"Invalid IP address: {ipAddress}");
                return CacheResult(ipAddress, "unknown");
            }

            var zone = FindMatchingZone(ip);
            return CacheResult(ipAddress, zone);
        }

        /// <summary>
        /// 根据区域获取所有IP段（CIDR格式，包括IPv4和IPv6）
        /// </summary>
        public IReadOnlyList<string> GetIpRangesByZone(string zone)
        {
            CheckDisposed();
            return _zoneToCidrs.TryGetValue(zone, out var cidrs) ?
                cidrs.ToList().AsReadOnly() :
                Array.Empty<string>();
        }

        /// <summary>
        /// 释放所有资源
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _ipZoneCache.Clear();
                _zoneToCidrs.Clear();
                lock (_ipv4Ranges) { _ipv4Ranges.Clear(); }
                lock (_ipv6Ranges) { _ipv6Ranges.Clear(); }
                _logger.LogInformation("IP geolocation service disposed");
            }
        }

        #endregion

        #region 私有方法

        private void LoadDefaultMappings()
        {
            // IPv4 内网地址段
            AddWellKnownMapping("10.0.0.0/8", "local");
            AddWellKnownMapping("172.16.0.0/12", "local");
            AddWellKnownMapping("192.168.0.0/16", "local");

            // IPv6 内网地址段
            AddWellKnownMapping("fc00::/7", "local");  // Unique Local Address (ULA)
            AddWellKnownMapping("fe80::/10", "local"); // Link-local addresses
        }

        private void AddWellKnownMapping(string cidr, string zone)
        {
            if (!TryAddMapping(cidr, zone))
            {
                _logger.LogWarning($"Failed to load built-in rule: {cidr} → {zone}");
            }
        }

        private void AddMappingInternal(IPAddress network, int prefixLength, string zone, string cidr)
        {
            var ipRange = new IpRange(network, prefixLength, zone);
            var ranges = network.AddressFamily == AddressFamily.InterNetwork ? _ipv4Ranges : _ipv6Ranges;

            lock (ranges)
            {
                ranges.Add(ipRange);
                ranges.Sort(); // 按前缀长度降序排序
            }

            // 使用线程安全的ConcurrentBag存储CIDR
            _zoneToCidrs.AddOrUpdate(
                zone,
                new ConcurrentBag<string> { cidr },
                (k, bag) => { bag.Add(cidr); return bag; });
        }

        private string FindMatchingZone(IPAddress ip)
        {
            var ranges = ip.AddressFamily == AddressFamily.InterNetwork ? _ipv4Ranges : _ipv6Ranges;

            lock (ranges)
            {
                foreach (var range in ranges)
                {
                    if (range.Contains(ip)) return range.Zone;
                }
            }

            return "unknown";
        }

        private bool TryParseCidr(string cidr, out IPAddress network, out int prefixLength)
        {
            network = null;
            prefixLength = 0;

            try
            {
                var parts = cidr.Split('/', 2);
                if (parts.Length != 2) return false;

                if (!IPAddress.TryParse(parts[0], out var addr)) return false;

                if (!int.TryParse(parts[1], out prefixLength)) return false;

                // 验证前缀长度有效性
                if (addr.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (prefixLength < 0 || prefixLength > 32) return false;
                }
                else if (addr.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    if (prefixLength < 0 || prefixLength > 128) return false;
                }
                else
                {
                    return false;
                }

                // 规范化网络地址（清除主机位）
                network = NormalizeNetworkAddress(addr, prefixLength);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IPAddress NormalizeNetworkAddress(IPAddress addr, int prefixLength)
        {
            var bytes = addr.GetAddressBytes();
            var maskBytes = addr.AddressFamily == AddressFamily.InterNetwork
                ? GetMaskBytes(32, prefixLength)
                : GetMaskBytes(128, prefixLength);

            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] &= maskBytes[i];
            }

            return new IPAddress(bytes);
        }

        private static byte[] GetMaskBytes(int addressBits, int prefixLength)
        {
            var maskBytes = new byte[addressBits / 8];
            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            for (int i = 0; i < fullBytes; i++)
                maskBytes[i] = 0xFF;

            if (remainingBits > 0)
                maskBytes[fullBytes] = (byte)(0xFF << (8 - remainingBits));

            return maskBytes;
        }

        private string CacheResult(string ipAddress, string zone)
        {
            var entry = new CacheEntry
            {
                Zone = zone,
                Expiration = DateTime.UtcNow.Add(_options.CacheExpiry)
            };

            // 限制缓存大小（优先淘汰过期条目）
            while (_ipZoneCache.Count >= _options.CacheSize)
            {
                var oldest = _ipZoneCache.OrderBy(e => e.Value.Expiration).FirstOrDefault();
                if (oldest.Equals(default)) break;
                _ipZoneCache.TryRemove(oldest.Key, out _);
            }

            _ipZoneCache.AddOrUpdate(ipAddress, entry, (k, v) => entry);
            return zone;
        }

        private void CheckDisposed()
        {
            if (_disposed != 0)
            {
                throw new ObjectDisposedException(nameof(DefaultIpGeoLocationService));
            }
        }

        private static void ValidateOptions(Options options)
        {
            if (options.CacheSize <= 0)
            {
                throw new ArgumentException("Cache size must be greater than 0", nameof(options.CacheSize));
            }
            if (options.CacheExpiry < TimeSpan.Zero)
            {
                throw new ArgumentException("Cache expiration time cannot be negative", nameof(options.CacheExpiry));
            }
        }

        #endregion

        #region 辅助类型

        /// <summary>
        /// 带过期时间的缓存条目
        /// </summary>
        private class CacheEntry
        {
            public string Zone { get; set; }
            public DateTime Expiration { get; set; }
        }

        #endregion

        /// <summary>
        /// 服务配置选项
        /// </summary>
        public class Options
        {
            /// <summary>
            /// 缓存最大容量（默认1000）
            /// </summary>
            public int CacheSize { get; set; } = 1000;

            /// <summary>
            /// 缓存过期时间（默认30分钟）
            /// </summary>
            public TimeSpan CacheExpiry { get; set; } = TimeSpan.FromMinutes(30);

            /// <summary>
            /// 是否启用日志（默认启用）
            /// </summary>
            public bool EnableLogging { get; set; } = true;
        }
    }
}