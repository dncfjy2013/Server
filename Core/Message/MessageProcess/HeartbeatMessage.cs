using Protocol;
using Server.Core.Common;
using Server.Core.Config;
using Server.Utils;
using System.Collections.Concurrent;

namespace Core.Message
{
    public class HeartbeatMessage
    {
        private ILogger _logger;
        private OutMessage _outMessage;
        public HeartbeatMessage(ILogger logger, OutMessage outMessage)
        {
            _logger = logger;
            _outMessage = outMessage;
        }
        /// <summary>
        /// 处理客户端心跳消息（接收并返回ACK）
        /// </summary>
        /// <param name="client">客户端配置对象</param>
        /// <param name="data">心跳消息数据</param>
        public async Task HandleHeartbeat(ClientConfig client, CommunicationData data)
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
            await _outMessage.SendInfoDate(client, ack);
            _logger.LogTrace($"Client {client.Id} heartbeat ACK sent successfully");
        }

    }
}
