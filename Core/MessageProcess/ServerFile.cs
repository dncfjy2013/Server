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
        // 文件传输处理优化相关代码

        // 用于存储正在进行的文件传输信息的并发字典
        // 键为文件传输的唯一标识（通常是文件名或文件ID），值为包含文件传输详细信息的对象
        // 采用 ConcurrentDictionary 是为了保证在多线程环境下对字典的操作是线程安全的，避免数据竞争问题
        private readonly ConcurrentDictionary<string, FileTransferInfo> _activeTransfers = new();

        // 异步锁，用于控制对文件相关操作的并发访问
        // 这里将最大并发数设置为 1，意味着同一时间只能有一个线程可以进入受该锁保护的代码块
        // 主要用于确保在文件传输过程中对文件的读写操作不会出现冲突，保证文件数据的完整性
        private SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1);

        // 定义一个事件，当文件传输完成时触发
        // 事件的订阅者可以通过注册一个接收字符串类型参数（通常是完成传输的文件的标识）的方法
        // 来处理文件传输完成的通知，实现模块间的解耦和消息传递
        // 例如，当文件传输完成后，可以通知其他模块进行文件的存储、处理或展示等操作
        public event Action<string> OnFileTransferCompleted;

        /// <summary>
        /// 处理文件传输请求（支持大文件分块传输、完整性校验、重传处理）
        /// </summary>
        /// <param name="client">客户端配置对象</param>
        /// <param name="data">文件传输数据（包含分块信息或完成通知）</param>
        private async Task HandleFileTransfer(ClientConfig client, CommunicationData data)
        {
            // 记录文件传输处理开始（细粒度调试）
            logger.LogTrace($"Client {client.Id} handling file transfer: FileId={data.FileId}, ChunkIndex={data.ChunkIndex}");

            // 统计接收数据量（内存占用计算）
            long receivedSize = MemoryCalculator.CalculateObjectSize(data);
            client.AddFileReceivedBytes(receivedSize);
            logger.LogDebug($"Client {client.Id} received {receivedSize} bytes for file {data.FileId}");

            // 申请文件操作锁（保证文件写入线程安全）
            await _fileLock.WaitAsync();
            logger.LogTrace($"Client {client.Id} acquired file lock for FileId={data.FileId}");

            try
            {
                FileTransferInfo transferInfo;

                // 处理文件传输完成事件
                if (data.Message == "FILE_COMPLETE")
                {
                    logger.LogInformation($"Client {client.Id} received file complete notification for FileId={data.FileId}");

                    if (_activeTransfers.TryRemove(data.FileId, out transferInfo))
                    {
                        // 1. 校验文件完整性（MD5哈希比对）
                        logger.LogDebug($"Client {client.Id} verifying file {transferInfo.FileName} integrity");
                        await VerifyFileIntegrity(transferInfo, data.Md5Hash);
                        logger.LogInformation($"Client {client.Id} file integrity verification passed for {transferInfo.FileName}");

                        // 2. 发送完成确认（高优先级ACK）
                        var completionAck = new CommunicationData
                        {
                            InfoType = InfoType.File,
                            AckNum = data.SeqNum,
                            Message = "FILE_COMPLETE_ACK",
                            FileId = data.FileId
                        };

                        logger.LogDebug($"Sending file complete ACK for FileId={data.FileId}");
                        await SendData(client, completionAck);
                        logger.LogInformation($"Client {client.Id} sent file complete ACK for {transferInfo.FileName}");

                        // 3. 统计发送数据量
                        long ackSize = MemoryCalculator.CalculateObjectSize(completionAck);
                        client.AddFileSentBytes(ackSize);
                        logger.LogDebug($"Sent {ackSize} bytes for file complete ACK");

                        // 4. 记录完成日志并触发事件
                        logger.LogInformation($"Client {client.Id} file transfer completed: {transferInfo.FilePath}");
                        logger.LogTrace($"Invoking OnFileTransferCompleted event for {transferInfo.FilePath}");
                        OnFileTransferCompleted?.Invoke(transferInfo.FilePath);
                    }
                    else
                    {
                        // 警告：未知文件ID的完成通知（可能是重复请求或攻击）
                        logger.LogWarning($"Client {client.Id} invalid file complete request: unknown FileId={data.FileId}");
                    }
                    return;
                }

                // 处理文件分块数据（非完成通知场景）
                #region 初始化或获取传输会话
                if (data.FileSize < 0)
                {
                    // 错误：非法文件大小（防御性校验）
                    logger.LogError($"Client {client.Id} invalid file size: {data.FileSize} (must be >= 0)");
                    return;
                }

                if (!_activeTransfers.TryGetValue(data.FileId, out transferInfo))
                {
                    // 新建传输会话（文件ID不存在时初始化）
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

                    logger.LogInformation($"Client {client.Id} new file transfer started: {transferInfo.FileName} (Size={data.FileSize} bytes)");
                    logger.LogDebug($"Creating directory for file: {Path.GetDirectoryName(transferInfo.FilePath)}");
                    Directory.CreateDirectory(Path.GetDirectoryName(transferInfo.FilePath));

                    logger.LogDebug($"Adding file transfer to active list: FileId={data.FileId}");
                    _activeTransfers.TryAdd(data.FileId, transferInfo);
                }
                else
                {
                    // 警告：重复初始化文件传输（可能是客户端重连）
                    logger.LogWarning($"Client {client.Id} file transfer already exists: FileId={data.FileId}");
                }
                #endregion

                #region 分块数据校验与存储
                // 快速MD5校验（提前过滤无效块）
                if (CalculateChunkHash(data.ChunkData.ToByteArray()) != data.ChunkMd5)
                {
                    logger.LogError($"Client {client.Id} chunk {data.ChunkIndex} MD5 mismatch (expected: {data.ChunkMd5}, actual: {CalculateChunkHash(data.ChunkData.ToByteArray())})");
                    return; // 丢弃无效块
                }
                logger.LogDebug($"Client {client.Id} chunk {data.ChunkIndex} MD5 verified successfully");

                // 存储分块数据（支持重传覆盖）
                logger.LogTrace($"Storing chunk {data.ChunkIndex} for FileId={data.FileId}");
                transferInfo.ReceivedChunks.AddOrUpdate(
                    data.ChunkIndex,
                    data.ChunkData.ToByteArray(),
                    (i, old) => data.ChunkData.ToByteArray() // 新块覆盖旧块（处理重传）
                );

                // 发送ACK（高优先级，确保客户端及时调整窗口）
                var ack = new CommunicationData
                {
                    InfoType = InfoType.Ack,
                    AckNum = data.SeqNum,
                    FileId = data.FileId,
                    ChunkIndex = data.ChunkIndex,
                    Priority = DataPriority.High // ACK使用最高优先级，避免阻塞
                };

                logger.LogInformation($"Client {client.Id} received chunk {data.ChunkIndex}/{transferInfo.TotalChunks} for {transferInfo.FileName}");
                await SendData(client, ack);
                logger.LogDebug($"Sent chunk ACK for ChunkIndex={data.ChunkIndex}, FileId={data.FileId}");

                // 检查是否所有块已接收
                if (transferInfo.ReceivedChunks.Count == transferInfo.TotalChunks)
                {
                    logger.LogInformation($"Client {client.Id} all chunks received for {transferInfo.FileName}, combining files...");
                    await CombineFileChunks(transferInfo); // 合并分块文件
                }
                #endregion

                // 统计发送数据量
                long ackDataSize = MemoryCalculator.CalculateObjectSize(ack);
                client.AddFileSentBytes(ackDataSize);
                logger.LogDebug($"Client {client.Id} sent {ackDataSize} bytes for chunk ACK");
            }
            catch (Exception ex)
            {
                // 致命错误：文件传输处理失败（清理会话并记录完整堆栈）
                logger.LogCritical($"Client {client.Id} file transfer failed: {ex.Message} {ex}");
                _activeTransfers.TryRemove(data.FileId, out _); // 清理无效会话
                throw;
            }
            finally
            {
                _fileLock.Release();
                logger.LogTrace($"Client {client.Id} released file lock for FileId={data.FileId}");
            }
        }
        /// <summary>
        /// 合并文件分块数据为完整文件（支持大文件异步写入）
        /// </summary>
        /// <param name="transferInfo">文件传输信息对象（包含分块数据和文件元信息）</param>
        private async Task CombineFileChunks(FileTransferInfo transferInfo)
        {
            logger.LogTrace($"Initiating file chunk combination: {transferInfo.FileName} (Total chunks={transferInfo.TotalChunks})");

            // 使用大缓冲区异步文件流（提升写入性能，减少I/O次数）
            using var fs = new FileStream(
                transferInfo.FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024 * 1024,  // 16MB缓冲区
                useAsync: true
            );
            logger.LogDebug($"Created FileStream for {transferInfo.FileName} with 16MB buffer");

            try
            {
                // 按块索引顺序写入文件（确保分块按顺序组装）
                for (int i = 0; i < transferInfo.TotalChunks; i++)
                {
                    logger.LogTrace($"Processing chunk {i}/{transferInfo.TotalChunks} for {transferInfo.FileName}");

                    // 检查块是否存在（防御性校验，处理乱序或缺失块）
                    if (!transferInfo.ReceivedChunks.TryGetValue(i, out var chunkData))
                    {
                        var errorMsg = $"Chunk {i} missing for file {transferInfo.FileName}, cannot combine";
                        logger.LogError(errorMsg);
                        throw new InvalidOperationException(errorMsg);
                    }

                    // 异步写入文件块（避免阻塞线程）
                    await fs.WriteAsync(chunkData, 0, chunkData.Length);
                    logger.LogDebug($"Wrote chunk {i} to {transferInfo.FileName} ({chunkData.Length} bytes)");
                }

                logger.LogInformation($"File {transferInfo.FileName} combined successfully ({transferInfo.TotalChunks} chunks)");
                logger.LogTrace($"File path: {transferInfo.FilePath}");
            }
            catch (IOException ex)
            {
                // 文件写入异常（磁盘故障、权限问题等）
                logger.LogCritical($"File write failed for {transferInfo.FileName}: {ex.Message} {ex}");
                throw;
            }
            catch (InvalidOperationException ex)
            {
                // 块缺失异常（业务逻辑错误）
                logger.LogError($"File combination failed: {ex.Message} {ex}");
                throw;
            }
        }

        /// <summary>
        /// 验证文件完整性（通过MD5哈希比对）
        /// </summary>
        /// <param name="transferInfo">文件传输信息对象</param>
        /// <param name="expectedHash">预期的MD5哈希值</param>
        private async Task VerifyFileIntegrity(FileTransferInfo transferInfo, string expectedHash)
        {
            logger.LogTrace($"Verifying integrity for file: {transferInfo.FileName} (Expected hash: {expectedHash})");

            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(transferInfo.FilePath))
            {
                try
                {
                    // 计算文件实际哈希值
                    var actualHash = BitConverter.ToString(await md5.ComputeHashAsync(stream))
                        .Replace("-", "").ToLowerInvariant();
                    logger.LogDebug($"Calculated hash for {transferInfo.FileName}: {actualHash}");

                    // 对比哈希值
                    if (actualHash != expectedHash)
                    {
                        // 哈希不匹配，删除文件并记录错误
                        File.Delete(transferInfo.FilePath);
                        logger.LogError($"File integrity failed for {transferInfo.FileName}: " +
                                       $"Expected hash={expectedHash}, Actual hash={actualHash}");
                        throw new InvalidDataException("File hash mismatch");
                    }

                    logger.LogInformation($"File {transferInfo.FileName} integrity verified successfully");
                }
                catch (IOException ex)
                {
                    // 文件读取异常（如文件被占用、磁盘错误）
                    logger.LogCritical($"Failed to read file {transferInfo.FileName} for verification: {ex.Message} {ex}");
                    throw;
                }
            }
        }

        /// <summary>
        /// 生成唯一文件路径（避免同名文件覆盖）
        /// </summary>
        /// <param name="originalPath">原始文件路径</param>
        /// <returns>唯一化后的文件路径</returns>
        private string GetUniqueFilePath(string originalPath)
        {
            logger.LogTrace($"Generating unique path for: {originalPath}");

            if (!File.Exists(originalPath))
            {
                logger.LogDebug($"Original path does not exist: {originalPath}, using directly");
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
                logger.LogTrace($"Checking path availability: {newPath}");
                counter++;
            } while (File.Exists(newPath));

            logger.LogInformation($"Generated unique path: {newPath} (Original path conflicted)");
            return newPath;
        }

        /// <summary>
        /// 计算文件分块的MD5哈希值
        /// </summary>
        /// <param name="data">分块字节数据</param>
        /// <returns>十六进制格式的MD5哈希字符串</returns>
        private string CalculateChunkHash(byte[] data)
        {
            logger.LogTrace($"Calculating chunk hash for data of size {data.Length} bytes");

            using var md5 = MD5.Create();
            try
            {
                var hash = md5.ComputeHash(data);
                var hashString = BitConverter.ToString(hash)
                    .Replace("-", "").ToLowerInvariant();
                logger.LogDebug($"Chunk hash calculated: {hashString}");
                return hashString;
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to calculate chunk hash: {ex.Message} {ex}");
                throw;
            }
        }
    }
    /// <summary>
    /// 用于跟踪文件传输状态的信息类，包含文件元数据、分块数据及存储路径等信息
    /// </summary>
    public class FileTransferInfo
    {
        /// <summary>
        /// 文件传输的唯一标识符（通常由客户端生成，用于关联分块数据和完成通知）
        /// </summary>
        public string FileId { get; set; }

        /// <summary>
        /// 原始文件名（包含文件扩展名，如"report.xlsx"）
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// 文件总大小（字节数，用于校验传输完整性和分块数量计算）
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// 文件分块总数（由客户端根据文件大小和分块策略计算得出）
        /// </summary>
        public int TotalChunks { get; set; }

        /// <summary>
        /// 单个分块的大小（字节数，除最后一个块外，其余块均为此大小）
        /// </summary>
        public int ChunkSize { get; set; }

        /// <summary>
        /// 文件在服务器端的存储路径（包含唯一化处理后的文件名，如"/data/files/user1/file_1.zip"）
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// 已接收的文件分块集合（键为块索引，值为分块字节数据）
        /// - 线程安全的并发字典，支持多线程环境下的分块写入和查询
        /// - 自动处理分块重传（新块覆盖旧块）
        /// </summary>
        public ConcurrentDictionary<int, byte[]> ReceivedChunks { get; set; } = new();
    }
}
