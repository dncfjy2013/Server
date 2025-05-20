using Server.Proxy.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Proxy.Common;

namespace Server.Proxy.Core
{
    sealed partial class AdvancedPortForwarder
    {
        private readonly ConcurrentDictionary<int, UdpClient> _udpClients = new(); // UDP 客户端集合
        private readonly ConcurrentDictionary<string, TargetServer> _udpMapServer = new();

        #region UDP协议处理模块
        /// <summary>
        /// 启动UDP端点监听
        /// 实现特点：
        /// • 使用UdpClient绑定监听地址端口
        /// • 基于限流器控制并发数据包处理（注：UDP无连接概念，此处限流器实际控制并发处理请求数）
        /// • 异步非阻塞接收数据包（支持取消操作）
        /// </summary>
        private async Task RunUdpEndpointAsync(EndpointConfig ep, CancellationToken ct)
        {
            // 创建UDP客户端并绑定监听端点（支持IPv4/IPv6，根据配置解析IP）
            var udpClient = new UdpClient(new IPEndPoint(IPAddress.Parse(ep.ListenIp), ep.ListenPort));

            // 确保端口唯一监听（避免UDP端口冲突）
            if (!_udpClients.TryAdd(ep.ListenPort, udpClient))
            {
                udpClient.Close(); // 释放资源
                throw new InvalidOperationException($"UDP端口 {ep.ListenPort} 已被占用");
            }

            _logger.LogInformation($"UDP监听器启动：端口 {ep.ListenPort}");

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 获取限流器租约（控制同时处理的数据包数量，防止内存溢出）
                    using var lease = await _portLimiters[ep.ListenPort].AcquireAsync(1, ct);
                    if (!lease.IsAcquired)
                    {
                        _logger.LogWarning($"UDP端口 {ep.ListenPort} 处理队列已满，丢弃数据包");
                        await Task.Delay(100, ct); // 短暂等待后重试（退避策略）
                        continue;
                    }

                    try
                    {
                        // 异步接收UDP数据包（包含客户端端点信息）
                        var result = await udpClient.ReceiveAsync(ct);
                        // 启动独立任务处理数据包（避免阻塞接收循环）
                        _ = HandleUdpPacketAsync(result, ep, ct);
                    }
                    catch (SocketException ex)
                    {
                        // 处理常见网络异常（如端口不可达、超时等）
                        _logger.LogWarning($"UDP接收失败：{ex.SocketErrorCode} - {ex.Message}");
                    }
                }
            }
            finally
            {
                // 清理资源（从集合移除并关闭客户端）
                _udpClients.TryRemove(ep.ListenPort, out _);
                udpClient.Close();
                _logger.LogInformation($"UDP监听器停止：端口 {ep.ListenPort}");
            }
        }

        /// <summary>
        /// 处理UDP数据包转发
        /// 实现逻辑：
        /// 1. 负载均衡选择目标服务器
        /// 2. 创建临时UdpClient发送数据包（注：UDP无连接，每次发送新建客户端可避免端口占用问题）
        /// 3. 记录转发日志（包含字节数和端点信息）
        /// </summary>
        private async Task HandleUdpPacketAsync(UdpReceiveResult result, EndpointConfig ep, CancellationToken ct)
        {
            try
            {
                // 负载均衡：选择当前连接数最少的目标服务器
                // 从缓存获取或选择新的目标服务器
                var clientKey = result.RemoteEndPoint.ToString();
                TargetServer target;
                if (ProxyConstant._isUseUDPMap)
                {
                     target = _udpMapServer.GetOrAdd(result.RemoteEndPoint.ToString(), _ => SelectServerAsync(ep).GetAwaiter().GetResult());
                }
                else
                {
                     target = await SelectServerAsync(ep);
                }
                // 使用using确保UdpClient及时释放资源
                using var client = new UdpClient();

                // 构造目标端点（IP+端口）
                var targetEndpoint = new IPEndPoint(IPAddress.Parse(target.Ip), target.TargetPort);
                // 发送数据包（使用2参数版本SendAsync，明确指定缓冲区和目标端点）
                await client.SendAsync(result.Buffer, result.Buffer.Length, targetEndpoint);

                _logger.LogDebug($"UDP转发完成：{result.RemoteEndPoint} → {target.Ip}:{target.TargetPort}，字节数 {result.Buffer.Length}");
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("UDP转发被取消");
            }
            catch (Exception ex)
            {
                // 记录所有未处理异常（如目标服务器不可达）
                _logger.LogError($"UDP转发失败：{ex.Message}");
            }
        }
        #endregion

        /// <summary>
        /// 停止所有UDP客户端
        /// 注意：UdpClient.Close() 会释放底层Socket资源
        /// </summary>
        private async Task StopUdpClientsAsync()
        {
            var tasks = new List<Task>();
            foreach (var client in _udpClients.Values)
            {
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        client.Close(); // 关闭UDP客户端，停止接收数据包
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"停止UDP客户端失败：{ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks);
            _udpClients.Clear();
        }
    }
}
