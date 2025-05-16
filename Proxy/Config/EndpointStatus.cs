using Server.Proxy.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Config
{
    /// <summary>
    /// 监听端点状态信息
    /// 作用：反映配置的监听端点是否正常运行，用于：
    /// ▶ 端点可用性检查（如端口是否被正确监听）
    /// ▶ 配置验证（启动后对比配置与实际运行的端点）
    /// ▶ 动态重新加载端点配置（如热更新时判断是否需要重启监听器）
    /// </summary>
    public class EndpointStatus
    {
        /// <summary>
        /// 端点监听的端口号
        /// </summary>
        public int ListenPort { get; set; }

        /// <summary>
        /// 端点协议类型（TCP/UDP/HTTP/SSL-TCP）
        /// </summary>
        public ConnectType Protocol { get; set; }

        /// <summary>
        /// 端点是否处于活跃状态
        /// 判断逻辑：
        /// isActive = 是否存在对应的监听器（TCPListener/HttpListener/UdpClient）
        /// 注：UDP监听器启动后即处于活跃状态，即使无数据包接收
        /// </summary>
        public bool IsActive { get; set; }
    }
}
