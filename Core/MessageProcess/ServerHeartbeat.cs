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
        private readonly int TimeoutSeconds = 45;
        // 心跳处理优化
        private async Task HandleHeartbeat(ClientConfig client, CommunicationData data)
        {
            client.AddReceivedBytes(MemoryCalculator.CalculateObjectSize(data));
            var ack = new CommunicationData
            {
                InfoType = InfoType.HeartBeat,
                Message = "ACK",
                AckNum = data.SeqNum
            };
            logger.LogInformation($"Client {client.Id} heartbeat");
            client.AddSentBytes(MemoryCalculator.CalculateObjectSize(ack));
            await SendData(client, ack);
        }

        private void CheckHeartbeats()
        {
            var now = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(TimeoutSeconds);

            foreach (var client in _clients)
            {
                if (now - client.Value.LastActivity > timeout)
                {
                    logger.LogWarning($"Client {client.Key} heartbeat timeout");
                    DisconnectClient(client.Key);
                    _clients.TryRemove(client.Key, out _);
                }
            }
        }
    }
}
