using Protocol;
using Server.Core.Config;
using Server.Utils;

namespace Server.Core
{
    partial class ServerInstance
    {
        // 心跳超时时间（秒），超过此时间未收到客户端活动则视为断开
        private readonly int TimeoutSeconds = 45; 

        /// <summary>
        /// 处理客户端心跳消息（接收并返回ACK）
        /// </summary>
        /// <param name="client">客户端配置对象</param>
        /// <param name="data">心跳消息数据</param>
        private async Task HandleHeartbeat(ClientConfig client, CommunicationData data)
        {
            // 统计接收数据量
            long receivedSize = MemoryCalculator.CalculateObjectSize(data);
            client.AddReceivedBytes(receivedSize);
            _logger.LogDebug($"Client {client.Id} received heartbeat (Size={receivedSize} bytes)");

            // 构造心跳响应
            var ack = new CommunicationData
            {
                InfoType = data.InfoType,
                Message = "ACK",
                AckNum = data.SeqNum,
                SeqNum = data.SeqNum,
            };

            // 记录正常心跳日志（Info级别）
            _logger.LogInformation($"Client {client.Id} heartbeat {data.SeqNum} received, sending ACK");

            // 统计发送数据量
            long sentSize = MemoryCalculator.CalculateObjectSize(ack);
            client.AddSentBytes(sentSize);
            _logger.LogDebug($"Client {client.Id} sent heartbeat ACK (Size={sentSize} bytes)");

            // 发送心跳响应
            await SendInfoDate(client, ack);
            _logger.LogTrace($"Client {client.Id} heartbeat ACK sent successfully");
        }

        /// <summary>
        /// 定期检查客户端心跳状态（超时断开无效连接）
        /// </summary>
        private void CheckHeartbeats()
        {
            var now = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(TimeoutSeconds);
            _logger.LogTrace($"Heartbeat check started (Timeout={TimeoutSeconds}s)");

            // 遍历所有客户端连接
            foreach (var client in _clients.ToList()) // 复制列表避免迭代时修改集合
            {
                var clientId = client.Key;
                var clientConfig = client.Value;

                // 计算客户端最后活动时间差
                var elapsed = now - clientConfig.LastActivity;
                _logger.LogDebug($"Client {clientId} last activity: {clientConfig.LastActivity} ({elapsed.TotalSeconds:F1}s ago)");

                if (elapsed > timeout)
                {
                    // 心跳超时，断开客户端连接
                    _logger.LogWarning($"Client {clientId} heartbeat timeout ({elapsed.TotalSeconds:F1}s > {TimeoutSeconds}s)");
                    DisconnectClient(clientId);

                    // 从客户端列表移除
                    if (_clients.TryRemove(clientId, out _))
                    {
                        _logger.LogInformation($"Client {clientId} removed due to heartbeat timeout");
                    }
                    else
                    {
                        _logger.LogError($"Failed to remove client {clientId} after timeout");
                    }
                }
            }

            _logger.LogTrace("Heartbeat check completed");
        }
    }
}
