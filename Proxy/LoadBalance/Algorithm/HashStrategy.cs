using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Server.Proxy.LoadBalance.Algorithm
{
    // 哈希策略实现（添加空键处理）
    public class HashStrategy : ILoadBalancingStrategy
    {
        private readonly Func<HttpRequestMessage, string> _hashKeySelector;

        public HashStrategy(Func<HttpRequestMessage, string> hashKeySelector)
        {
            _hashKeySelector = hashKeySelector ?? throw new ArgumentNullException(nameof(hashKeySelector));
        }

        public TargetServer SelectServer(List<TargetServer> servers, object context = null)
        {
            if (servers.Count == 0)
                throw new ArgumentException("No servers available", nameof(servers));

            // 提取哈希键并处理空值
            string key = _hashKeySelector(context as HttpRequestMessage) ?? "empty-key";

            // 一致性哈希算法（示例：使用FNV-1a哈希）
            uint hash = Fnv1aHash(key);
            int serverIndex = (int)(hash % servers.Count);

            return servers[serverIndex];
        }

        // FNV-1a哈希算法实现（64位版本）
        private uint Fnv1aHash(string input)
        {
            const uint FnvPrime = 0x01000193;
            const uint FnvOffsetBias = 0x811C9DC5;

            uint hash = FnvOffsetBias;
            foreach (byte b in Encoding.UTF8.GetBytes(input))
            {
                hash ^= b;
                hash *= FnvPrime;
            }
            return hash;
        }
    }
}