using Server.Proxy.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Config
{
    /// <summary>
    /// 端口转发器整体性能指标
    /// 作用：提供转发器运行时的全局状态监控数据，可用于：
    /// ▶ 实时仪表盘展示
    /// ▶ 性能瓶颈分析（如连接数突增）
    /// ▶ 健康检查接口（如HTTP端点返回JSON指标）
    /// </summary>
    public class PortForwarderMetrics
    {
        /// <summary>
        /// 当前所有协议的活跃连接总数
        /// 计算方式：各目标服务器的ActiveConnections字段之和
        /// 注意：UDP无连接概念，此处统计仅包含TCP/HTTP连接
        /// </summary>
        public int ActiveConnections { get; set; }

        /// <summary>
        /// 各目标服务器的连接性能指标列表
        /// 键：{TargetServer.Ip}:{TargetServer.TargetPort}
        /// 包含数据：
        /// • 活跃连接数（ActiveConnections）
        /// • 总连接数（TotalConnections，自转发器启动以来的累计值）
        /// • 最后一次连接活动时间（LastActivity）
        /// </summary>
        public List<ConnectionMetrics> ConnectionMetrics { get; set; } = new();

        /// <summary>
        /// 各监听端点的状态列表
        /// 每个元素对应一个EndpointConfig配置项，包含：
        /// • 监听端口（ListenPort）
        /// • 协议类型（Protocol）
        /// • 是否处于活跃状态（IsActive，是否至少有一个对应协议的监听器在运行）
        /// </summary>
        public List<EndpointStatus> EndpointStatus { get; set; } = new();
    }
}
