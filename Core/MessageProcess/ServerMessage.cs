using Protocol;
using Server.Extend;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core
{
    partial class Server
    {

        // 普通消息处理方法，用于处理客户端发送的普通消息，并返回确认消息
        private async Task HandleNormalMessage(ClientConfig client, CommunicationData data)
        {
            // 记录方法开始处理，使用Trace级别日志，用于开发调试时跟踪流程
            logger.LogTrace($"Start handling normal message from client {client.Id}, SeqNum: {data.SeqNum}");

            // 计算接收到的消息数据大小，并更新客户端接收字节数统计
            long receivedSize = MemoryCalculator.CalculateObjectSize(data);
            client.AddReceivedBytes(receivedSize);
            // 使用Debug级别日志记录接收到的消息大小，方便调试和性能分析
            logger.LogDebug($"Client {client.Id} received a normal message of size {receivedSize} bytes, SeqNum: {data.SeqNum}");

            // 构造确认消息对象，用于向客户端发送确认信息
            var ack = new CommunicationData
            {
                InfoType = InfoType.Normal,
                AckNum = data.SeqNum,
                Message = "ACK"
            };

            try
            {
                // 异步发送确认消息给客户端
                await SendData(client, ack);
                // 记录确认消息发送成功，使用Trace级别日志
                logger.LogTrace($"Successfully sent ACK to client {client.Id} for SeqNum: {data.SeqNum}");
            }
            catch (Exception ex)
            {
                // 若发送确认消息时出现异常，使用Error级别日志记录详细错误信息
                logger.LogError($"Failed to send ACK to client {client.Id} for SeqNum: {data.SeqNum}. Error: {ex.Message} {ex}");
                return;
            }

            // 计算发送的确认消息数据大小，并更新客户端发送字节数统计
            long sentSize = MemoryCalculator.CalculateObjectSize(ack);
            client.AddSentBytes(sentSize);
            // 使用Debug级别日志记录发送的确认消息大小
            logger.LogDebug($"Client {client.Id} sent an ACK message of size {sentSize} bytes, SeqNum: {data.SeqNum}");

            // 记录普通消息处理完成，使用Info级别日志，用于正常业务流程记录
            logger.LogInformation($"Client {client.Id} ACK: {data.SeqNum} Message: {data.Message}");
            logger.LogInformation($"Client {client.Id} ACK: {data.SeqNum} Message: {data.Message} (English: Client {client.Id} ACK: {data.SeqNum} Message: {data.Message})");

            // 记录方法处理结束，使用Trace级别日志
            logger.LogTrace($"Finished handling normal message from client {client.Id}, SeqNum: {data.SeqNum}");
        }

    }
}
