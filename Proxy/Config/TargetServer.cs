using Server.Proxy.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace Server.Proxy.Config
{
    /// <summary>
    /// 目标服务器配置模型
    /// 作用：定义转发器将流量转发至的后端服务器参数，支持：
    /// ▶ 多协议后端（TCP/UDP/HTTP/SSL）
    /// ▶ 端口映射（ListenPort → TargetPort）
    /// ▶ 安全传输（TLS证书配置）
    /// ▶ 连接统计（原子操作确保线程安全）
    /// </summary>
    public class TargetServer
    {
        /// <summary>
        /// 当前活跃连接数（使用Interlocked确保线程安全）
        /// 注意：
        /// • 仅统计正在进行数据传输的连接
        /// • 连接池中的空闲连接不计入此数值
        /// </summary>
        private int _currentConnections;

        /// <summary>
        /// 目标服务器IP地址
        /// 支持格式：
        /// • IPv4：如 "192.168.1.100"
        /// • IPv6：如 "[2001:db8::1]"（需包含方括号）
        /// • 域名：如 "api.example.com"（需确保DNS可解析）
        /// </summary>
        public string Ip { get; }

        /// <summary>
        /// 目标服务器源端口（通常与ListenPort相同）
        /// 用途：
        /// • 负载均衡决策（如按源端口分流）
        /// • 日志记录（标识不同服务）
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// 目标服务器实际监听端口
        /// 示例：前端监听80端口，后端服务运行在8080，则TargetPort=8080
        /// </summary>
        public int TargetPort { get; }

        /// <summary>
        /// 后端服务协议类型
        /// 影响转发行为：
        /// • TCP：直接转发TCP流
        /// • SslTcp：在TCP基础上建立SSL/TLS连接
        /// • Udp：UDP数据包转发
        /// • Http：HTTP协议处理（解析请求行、头、体）
        /// </summary>
        public ConnectType BackendProtocol { get; set; } = ConnectType.Tcp;

        /// <summary>
        /// 客户端证书（用于SSL/TLS连接）
        /// 适用场景：
        /// • 后端服务需要客户端证书验证
        /// • 双向TLS认证场景
        /// </summary>
        public X509Certificate2 ClientCertificate { get; set; }

        /// <summary>
        /// 当前活跃连接数（线程安全获取）
        /// 注意：此属性为只读，使用Interlocked保证可见性
        /// </summary>
        public int CurrentConnections => _currentConnections;

        /// <summary>
        /// 初始化目标服务器配置
        /// </summary>
        /// <param name="ip">目标服务器IP地址或域名</param>
        /// <param name="port">源端口（通常与ListenPort相同）</param>
        /// <param name="targetPort">目标服务器实际监听端口</param>
        public TargetServer(string ip, int port, int targetPort)
        {
            Ip = ip;
            Port = port;
            TargetPort = targetPort;
        }

        /// <summary>
        /// 原子操作：增加活跃连接数
        /// 线程安全实现：使用Interlocked避免竞态条件
        /// 调用时机：每次成功建立到目标服务器的连接时
        /// </summary>
        public void Increment() => Interlocked.Increment(ref _currentConnections);

        /// <summary>
        /// 原子操作：减少活跃连接数
        /// 线程安全实现：使用Interlocked确保操作的原子性
        /// 调用时机：每次关闭到目标服务器的连接时
        /// </summary>
        public void Decrement() => Interlocked.Decrement(ref _currentConnections);
    }
}
