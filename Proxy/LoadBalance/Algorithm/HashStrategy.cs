using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Proxy.LoadBalance.Algorithm
{
    // 哈希策略（支持自定义哈希键）
    public class HashStrategy : ILoadBalancingStrategy
    {
        private readonly Func<HttpRequestMessage, string> _hashKeySelector;

        public HashStrategy(Func<HttpRequestMessage, string> hashKeySelector) // 移除多余逗号
        {
            _hashKeySelector = hashKeySelector ?? throw new ArgumentNullException(nameof(hashKeySelector));
        }

        /// <summary>
        /// 选择目标服务器（支持从上下文中提取哈希键）
        /// </summary>
        /// <param name="servers">可用服务器列表</param>
        /// <param name="context">请求上下文（必须为 HttpRequestMessage 类型）</param>
        public TargetServer SelectServer(List<TargetServer> servers, object context)
        {
            if (!servers.Any()) throw new InvalidOperationException("服务器列表为空");
            if (context == null) throw new ArgumentNullException(nameof(context));

            // 关键修复：检查并转换上下文类型
            if (context is not HttpRequestMessage request)
            {
                throw new ArgumentException($"上下文必须是 {nameof(HttpRequestMessage)} 类型", nameof(context));
            }

            try
            {
                var hashKey = _hashKeySelector(request); // 使用转换后的请求对象
                var hashCode = Math.Abs(hashKey.GetHashCode());
                var index = hashCode % servers.Count;
                return servers[index];
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("哈希键生成失败", ex);
            }
        }
    }
}