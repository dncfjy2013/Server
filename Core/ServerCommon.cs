using Google.Protobuf;
using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core
{
    partial class Server
    {
        private async Task<bool> ReadFullAsync(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset);
                if (read == 0)
                {
                    logger.LogWarning($"Connection closed while reading. Expected: {count}, Read: {offset}");
                    return false;
                }
                offset += read;
                logger.LogTrace($"Read progress: {offset}/{count} bytes");
            }
            return true;
        }

        private async Task<bool> SendData(ClientConfig client, CommunicationData data)
        {
            try
            {
                // 1. 验证参数
                if (client?.Socket == null || !client.Socket.Connected || data == null)
                {
                    logger.LogWarning($"Client {client.Id} Invalid parameters for SendData");
                    return false;
                }

                // 2. 获取配置(假设config是类成员变量或通过client获取)
                //var config = config ?? new ProtocolConfiguration();

                // 3. 创建协议数据包
                var packet = new ProtocolPacketWrapper(
                    new ProtocolPacket()
                    {
                        Header = new ProtocolHeader { Version = 0x01, Reserved = ByteString.CopyFrom(new byte[3]) },
                        Data = data
                    },
                    config);

                // 4. 序列化为字节数组
                byte[] protocolBytes;
                try
                {
                    protocolBytes = packet.ToBytes();
                }
                catch (Exception ex)
                {
                    logger.LogError($"Client {client.Id} Packet serialization failed: {ex.Message}");
                    return false;
                }

                // 5. 发送数据(确保发送完整)
                int totalSent = 0;
                while (totalSent < protocolBytes.Length)
                {
                    int sent = await client.Socket.SendAsync(
                        new ArraySegment<byte>(protocolBytes, totalSent, protocolBytes.Length - totalSent),
                        SocketFlags.None);

                    if (sent == 0)
                    {
                        logger.LogWarning($"Client {client.Id} Connection closed during send");
                        return false;
                    }

                    totalSent += sent;
                }

                // 6. 更新统计
                client.AddSentBytes(protocolBytes.Length);
                return true;
            }
            catch (SocketException sex)
            {
                logger.LogError($"Client {client.Id} Socket error in SendData: {sex.SocketErrorCode} - {sex.Message}");
                DisconnectClient(client.Id);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogCritical($"Client {client.Id} Unexpected error in SendData: {ex.Message}");
                return false;
            }
        }
    }
}
