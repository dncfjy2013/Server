using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Config
{
    /// <summary>
    /// 单个目标服务器的连接性能指标
    /// 作用：追踪转发器与目标服务器之间的连接状态，支持：
    /// ▶ 负载均衡算法决策（如最小连接数）
    /// ▶ 服务器健康度评估（通过LastActivity判断是否超时）
    /// ▶ 连接泄漏检测（TotalConnections异常增长）
    /// </summary>
    public class ConnectionMetrics
    {
        /// <summary>
        /// 目标服务器标识（格式：IP:Port）
        /// 示例："192.168.1.100:8080"
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// 当前与该目标服务器的活跃连接数
        /// 注：TCP连接池中的空闲连接不计入活跃连接（仅处于数据传输中的连接）
        /// </summary>
        public int ActiveConnections { get; set; }

        /// <summary>
        /// 自转发器启动以来连接到该目标服务器的总次数
        /// 每次成功建立连接（包括从连接池获取的有效连接）均计数+1
        /// </summary>
        public int TotalConnections { get; set; }

        /// <summary>
        /// 该目标服务器最后一次数据传输的时间（UTC时间）
        /// 用途：
        /// • 检测服务器是否无响应（如LastActivity超过阈值则标记为不可用）
        /// • 实现连接超时机制（如空闲连接自动关闭）
        /// </summary>
        public DateTime LastActivity { get; set; }
    }
}
