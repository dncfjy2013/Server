using Protocol;
using Server.Extend;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core
{
    partial class Server
    {
        // 文件传输处理优化// 文件传输处理优化
        private readonly ConcurrentDictionary<string, FileTransferInfo> _activeTransfers = new();
        private SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1); // 异步锁
        // 可选：定义文件完成事件
        public event Action<string> OnFileTransferCompleted;

        private async Task HandleFileTransfer(ClientConfig client, CommunicationData data)
        {
            client.AddFileReceivedBytes(MemoryCalculator.CalculateObjectSize(data));
            await _fileLock.WaitAsync();
            try
            {
                FileTransferInfo transferInfo;
                // 处理文件传输完成消息
                if (data.Message == "FILE_COMPLETE")
                {
                    if (_activeTransfers.TryRemove(data.FileId, out transferInfo))
                    {
                        // 验证文件完整性
                        await VerifyFileIntegrity(transferInfo, data.Md5Hash);

                        // 发送完成确认
                        var completionAck = new CommunicationData
                        {
                            InfoType = InfoType.File,
                            AckNum = data.SeqNum,
                            Message = "FILE_COMPLETE_ACK",
                            FileId = data.FileId
                        };

                        client.AddFileSentBytes(MemoryCalculator.CalculateObjectSize(completionAck));

                        await SendData(client, completionAck);

                        logger.LogInformation($"Client {client.Id} File {transferInfo.FileName} transfer completed successfully");

                        // 触发文件完成事件
                        OnFileTransferCompleted?.Invoke(transferInfo.FilePath);
                    }
                    else
                    {
                        logger.LogWarning($"Client {client.Id} Received FILE_COMPLETE for unknown file ID: {data.FileId}");
                    }
                    return;
                }
                else
                {
                    // 初始化传输会话（支持20G+文件，检查文件大小合法性）
                    if (data.FileSize < 0)
                    {
                        logger.LogCritical($"Client {client.Id} 非法文件大小 {nameof(data.FileSize)}");
                        return;
                    }

                    // 处理文件块数据
                    if (!_activeTransfers.TryGetValue(data.FileId, out transferInfo))
                    {
                        // 初始化新的文件传输
                        transferInfo = new FileTransferInfo
                        {
                            FileId = data.FileId,
                            FileName = data.FileName,
                            FileSize = data.FileSize,
                            TotalChunks = data.TotalChunks,
                            ChunkSize = data.ChunkData.Length,
                            ReceivedChunks = new ConcurrentDictionary<int, byte[]>(),
                            FilePath = GetUniqueFilePath(Path.Combine(client.FilePath, data.FileName))
                        };
                        Directory.CreateDirectory(Path.GetDirectoryName(transferInfo.FilePath));
                        _activeTransfers.TryAdd(data.FileId, transferInfo);
                    }

                    // 快速校验块MD5（提前终止无效块处理）
                    if (CalculateChunkHash(data.ChunkData.ToByteArray()) != data.ChunkMd5)
                    {
                        logger.LogWarning($"Client {client.Id} 块 {data.ChunkIndex} MD5校验失败，丢弃");
                        return;
                    }

                    // 存储块数据（支持并发写入，覆盖旧块）
                    transferInfo.ReceivedChunks.AddOrUpdate(
                        data.ChunkIndex,
                        data.ChunkData.ToByteArray(),
                        (i, old) => data.ChunkData.ToByteArray() // 新块覆盖旧块（处理重传）
                    );

                    // 立即发送ACK（高优先级，确保客户端及时释放窗口）
                    var ack = new CommunicationData
                    {
                        InfoType = InfoType.Ack,
                        AckNum = data.SeqNum,
                        FileId = data.FileId,
                        ChunkIndex = data.ChunkIndex,
                        Priority = DataPriority.High // ACK使用最高优先级
                    };

                    logger.LogInformation($"Client {client.Id} Received chunk {data.ChunkIndex} of {data.TotalChunks} for file {data.FileId}");

                    await SendData(client, ack);

                    // 如果所有块都已接收，组合文件
                    if (transferInfo.ReceivedChunks.Count == transferInfo.TotalChunks)
                    {
                        await CombineFileChunks(transferInfo);
                    }

                    client.AddFileSentBytes(MemoryCalculator.CalculateObjectSize(ack));
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Client {client.Id} Error processing file transfer: {ex.Message}");
                _activeTransfers.TryRemove(data.FileId, out _); // 清理无效会话
                throw;
            }
            finally
            {
                _fileLock.Release();
            }
        }
        private async Task CombineFileChunks(FileTransferInfo transferInfo)
        {
            // 使用16MB缓冲区异步写入（提升磁盘写入速度）
            using var fs = new FileStream(
                transferInfo.FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024 * 1024,  // 大缓冲区减少磁盘I/O
                useAsync: true
            );

            // 按块索引顺序写入（处理乱序块，确保顺序正确）
            for (int i = 0; i < transferInfo.TotalChunks; i++)
            {
                if (!transferInfo.ReceivedChunks.TryGetValue(i, out var chunkData))
                {
                    throw new InvalidOperationException($"块 {i} 缺失，文件 {transferInfo.FileName} 组装失败");
                }
                await fs.WriteAsync(chunkData, 0, chunkData.Length); // 异步写入
            }
            logger.LogInformation($"文件 {transferInfo.FileName} 组装完成（{transferInfo.TotalChunks} 块）");
        }

        private async Task VerifyFileIntegrity(FileTransferInfo transferInfo, string expectedHash)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(transferInfo.FilePath))
            {
                var actualHash = BitConverter.ToString(await md5.ComputeHashAsync(stream))
                    .Replace("-", "").ToLowerInvariant();

                if (actualHash != expectedHash)
                {
                    File.Delete(transferInfo.FilePath);
                    logger.LogWarning($"File integrity check failed for {transferInfo.FileName}");
                }
            }
        }

        private string GetUniqueFilePath(string originalPath)
        {
            if (!File.Exists(originalPath))
            {
                logger.LogWarning($"not exit {originalPath}");
                return originalPath;
            }

            var directory = Path.GetDirectoryName(originalPath);
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
            var extension = Path.GetExtension(originalPath);

            int counter = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExtension}_{counter}{extension}");
                counter++;
            } while (File.Exists(newPath));

            return newPath;
        }

        private string CalculateChunkHash(byte[] data)
        {
            using var md5 = MD5.Create();
            return BitConverter.ToString(md5.ComputeHash(data))
                .Replace("-", "").ToLowerInvariant();
        }
    }

    // 文件传输信息类
    public class FileTransferInfo
    {
        public string FileId { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public int TotalChunks { get; set; }
        public int ChunkSize { get; set; }
        public string FilePath { get; set; }
        public ConcurrentDictionary<int, byte[]> ReceivedChunks { get; set; }
    }
}
