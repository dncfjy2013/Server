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
        /// <summary>
        /// 异步读取指定数量的字节到缓冲区中，确保读取的字节数达到指定的数量。
        /// </summary>
        /// <param name="stream">要读取数据的流。</param>
        /// <param name="buffer">用于存储读取数据的缓冲区。</param>
        /// <param name="count">需要读取的字节数。</param>
        /// <returns>如果成功读取指定数量的字节，返回 true；否则返回 false。</returns>
        private async Task<bool> ReadFullAsync(Stream stream, byte[] buffer, int count)
        {
            // 初始化偏移量，用于记录已经读取的字节数
            int offset = 0;
            logger.LogTrace($"Starting to read {count} bytes from the stream.");

            // 循环读取数据，直到读取的字节数达到指定数量
            while (offset < count)
            {
                // 异步读取数据到缓冲区中
                int read = await stream.ReadAsync(buffer, offset, count - offset);
                logger.LogTrace($"Read {read} bytes from the stream at offset {offset}.");

                // 如果读取的字节数为 0，说明连接已经关闭
                if (read == 0)
                {
                    logger.LogWarning($"Connection closed while reading. Expected: {count} bytes, Read: {offset} bytes.");
                    return false;
                }

                // 更新偏移量
                offset += read;
                logger.LogTrace($"Read progress: {offset}/{count} bytes.");
            }

            logger.LogTrace($"Successfully read {count} bytes from the stream.");
            return true;
        }

        /// <summary>
        /// 异步向客户端发送数据，确保数据完整发送，并处理可能出现的异常。
        /// </summary>
        /// <param name="client">客户端配置对象，包含客户端的套接字信息。</param>
        /// <param name="data">要发送的通信数据。</param>
        /// <returns>如果数据发送成功，返回 true；否则返回 false。</returns>
        private async Task<bool> SendData(ClientConfig client, CommunicationData data)
        {
            logger.LogTrace($"Starting to send data to client {client.Id}.");

            try
            {
                // 1. 验证参数
                // 检查客户端套接字是否存在、是否连接以及数据是否为空
                if (client?.Socket == null || !client.Socket.Connected || data == null)
                {
                    logger.LogWarning($"Client {client?.Id} Invalid parameters for SendData. Client socket is null or not connected, or data is null.");
                    return false;
                }

                logger.LogTrace($"Client {client.Id} Parameters are valid. Proceeding to create protocol packet.");

                // 2. 获取配置(假设config是类成员变量或通过client获取)
                //var config = config ?? new ProtocolConfiguration();

                // 3. 创建协议数据包
                // 根据传入的数据和协议配置创建协议数据包
                var packet = new ProtocolPacketWrapper(
                    new ProtocolPacket()
                    {
                        Header = new ProtocolHeader { Version = 0x01, Reserved = ByteString.CopyFrom(new byte[3]) },
                        Data = data
                    },
                    config);

                logger.LogTrace($"Client {client.Id} Protocol packet created. Proceeding to serialize it.");

                // 4. 序列化为字节数组
                byte[] protocolBytes;
                try
                {
                    // 将协议数据包序列化为字节数组
                    protocolBytes = packet.ToBytes();
                    logger.LogTrace($"Client {client.Id} Protocol packet serialized to {protocolBytes.Length} bytes.");
                }
                catch (Exception ex)
                {
                    logger.LogError($"Client {client.Id} Packet serialization failed: {ex.Message}. Stack trace: {ex.StackTrace}");
                    return false;
                }

                // 5. 发送数据(确保发送完整)
                // 初始化已发送的字节数
                int totalSent = 0;
                logger.LogTrace($"Client {client.Id} Starting to send {protocolBytes.Length} bytes of data.");

                // 循环发送数据，直到所有数据都发送完毕
                while (totalSent < protocolBytes.Length)
                {
                    // 异步发送数据
                    int sent = await client.Socket.SendAsync(
                        new ArraySegment<byte>(protocolBytes, totalSent, protocolBytes.Length - totalSent),
                        SocketFlags.None);

                    logger.LogTrace($"Client {client.Id} Sent {sent} bytes of data at offset {totalSent}.");

                    // 如果发送的字节数为 0，说明连接已经关闭
                    if (sent == 0)
                    {
                        logger.LogWarning($"Client {client.Id} Connection closed during send. Sent {totalSent} bytes out of {protocolBytes.Length} bytes.");
                        return false;
                    }

                    // 更新已发送的字节数
                    totalSent += sent;
                }

                logger.LogTrace($"Client {client.Id} Successfully sent {protocolBytes.Length} bytes of data.");

                // 6. 更新统计
                // 更新客户端发送的字节数统计信息
                client.AddSentBytes(protocolBytes.Length);
                logger.LogTrace($"Client {client.Id} Updated sent bytes statistics. Total sent: {client.BytesSent} bytes.");

                return true;
            }
            catch (SocketException sex)
            {
                logger.LogError($"Client {client.Id} Socket error in SendData: {sex.SocketErrorCode} - {sex.Message}. Stack trace: {sex.StackTrace}");
                // 断开客户端连接
                DisconnectClient(client.Id);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogCritical($"Client {client.Id} Unexpected error in SendData: {ex.Message}. Stack trace: {ex.StackTrace}");
                return false;
            }
        }
    }
}
