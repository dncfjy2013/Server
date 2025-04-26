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
        // 普通消息处理
        private async Task HandleNormalMessage(ClientConfig client, CommunicationData data)
        {
            client.AddReceivedBytes(MemoryCalculator.CalculateObjectSize(data));
            var ack = new CommunicationData
            {
                InfoType = InfoType.Normal,
                AckNum = data.SeqNum,
                Message = "ACK"
            };
            await SendData(client, ack);
            client.AddSentBytes(MemoryCalculator.CalculateObjectSize(ack));
            logger.LogInformation($"Client {client.Id} ACK: {data.SeqNum} Message: {data.Message}");
        }

    }
}
