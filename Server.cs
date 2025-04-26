using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using Server.Utils;
using Server.Common;
using Server.Extend;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Security.Authentication;
using Microsoft.VisualBasic;
using System.Security.Cryptography;
using System.Threading.Channels;
using Server.Client;
using System.Buffers;
using Protocol;
using Google.Protobuf;

namespace Server
{
    public class Server
    {
        private readonly int _port;
        private readonly int _sslPort;
        private TcpListener _sslListener;
        private X509Certificate2 _serverCert;
        private Socket _listener;
        private readonly ConcurrentDictionary<int, ClientConfig> _clients = new();
        private int _nextClientId;
        private readonly Timer _heartbeatTimer;
        private readonly Timer _trafficMonitorTimer;
        private bool _isRunning;
        private readonly int HeartbeatInterval = 10000;
        private readonly int TimeoutSeconds = 45;
        private readonly int BufferSize = 2048;
        private readonly int ListenMax = 100;
        private int _monitorInterval = 5000; // 默认监控间隔
        private readonly CancellationTokenSource _cts = new();
        private readonly object _lock = new(); // 用于线程安全日志记录
        Logger logger = new Logger();
        private readonly TrafficMonitor _trafficMonitor;
        // 新增文件传输相关字段
        //private readonly ConcurrentDictionary<string, FileTransferInfo> _activeTransfers = new();
        private SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1); // 异步锁
        private Channel<ClientMessage> _messageHighQueue = Channel.CreateUnbounded<ClientMessage>();
        private Channel<ClientMessage> _messageMediumQueue = Channel.CreateUnbounded<ClientMessage>();
        private Channel<ClientMessage> _messagelowQueue = Channel.CreateUnbounded<ClientMessage>();
        private readonly CancellationTokenSource _processingCts = new();
        private readonly int MaxQueueSize = int.MaxValue;

        private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;

        public Server(int port, int sslPort, string certPath = null)
        {
            _port = port;
            _sslPort = sslPort;

            // 加载SSL证书
            if (!string.IsNullOrEmpty(certPath))
            {
                _serverCert = new X509Certificate2(certPath);
                logger.LogWarning("SSL is not verified");
            }
            
            // 初始化流量监控器（关键修改）
            _trafficMonitor = new TrafficMonitor(_clients, _monitorInterval);

            _heartbeatTimer = new Timer(_ => CheckHeartbeats(), null, Timeout.Infinite, Timeout.Infinite);
            _trafficMonitorTimer = new Timer(_ => _trafficMonitor.Monitor(), null, Timeout.Infinite, Timeout.Infinite);

            logger.LogInformation("Start Sever");

            StartProcessing();
        }

        public void Start(bool enableMonitoring = false)
        {
            _isRunning = true;
            _heartbeatTimer.Change(0, HeartbeatInterval);

            if (enableMonitoring)
            {
                _trafficMonitor.ModifyEnable(true);
                _trafficMonitorTimer.Change(0, _monitorInterval);
            }

            // 启动普通端口监听
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.Bind(new IPEndPoint(IPAddress.Any, _port));
            _listener.Listen(ListenMax);
            logger.LogInformation($"Socket started on port {_port} with monitoring {(enableMonitoring ? "enabled" : "disabled")}.");

            // 启动SSL端口监听
            if (_serverCert != null)
            {
                _sslListener = new TcpListener(IPAddress.Any, _sslPort);
                _sslListener.Start();
                logger.LogInformation($"SSL started on port {_port} with monitoring {(enableMonitoring ? "enabled" : "disabled")}.");
                _ = AcceptSslClients();
            }

            AcceptSocketClients();
        }

        // 新增SSL客户端接受方法
        private async Task AcceptSslClients()
        {
            while (_isRunning)
            {
                try
                {
                    var sslClient = await _sslListener.AcceptTcpClientAsync();
                    var sslStream = new SslStream(sslClient.GetStream(), false);

                    // 配置SSL参数
                    var sslOptions = new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _serverCert,
                        ClientCertificateRequired = true, // 强制客户端证书验证
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    };

                    await sslStream.AuthenticateAsServerAsync(sslOptions);

                    var clientId = Interlocked.Increment(ref _nextClientId);
                    var client = new ClientConfig(clientId, sslStream);
                    _clients[clientId] = client;

                    logger.LogInformation($"SSL Client {clientId} connected: {sslClient.Client.RemoteEndPoint}");
                    _ = HandleClient(client);
                }
                catch (Exception ex)
                {
                    logger.LogError($"SSL accept error: {ex.Message}");
                }
            }
        }

        private async void AcceptSocketClients()
        {
            while (_isRunning)
            {
                try
                {
                    var clientSocket = await _listener.AcceptAsync();
                    var clientId = Interlocked.Increment(ref _nextClientId);

                    var client = new ClientConfig(clientId, clientSocket);
                    _clients[clientId] = client;

                    logger.LogInformation($"Socket Client {clientId} connected: {clientSocket.RemoteEndPoint}");

                    _ = HandleClient(client);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // 正常关闭时忽略异常
                }
                catch (Exception ex)
                {
                    logger.LogError($"Socket Accept error: {ex.Message}");
                }
            }
        }

        // 修改后的HandleClient方法（生产者）
        private async Task HandleClient(ClientConfig client)
        {
            byte[] headerBuffer = new byte[8];

            try
            {
                var buffer = new byte[BufferSize];
                Stream stream = client.Socket != null
                    ? new NetworkStream(client.Socket)
                    : client.SslStream;
                //using var buffer = _memoryPool.Rent(BufferSize);
                while (_isRunning)
                {
                    try
                    {
                        // 1. 接收头部
                        if (!await ReadFullAsync(stream, headerBuffer, 8))
                        {
                            logger.LogWarning($"Client {client.Id} disconnected while reading header");
                            return;
                        }

                        // 2. 解析头部
                        if (!ProtocolHeaderExtensions.TryFromBytes(headerBuffer, out var header))
                        {
                            logger.LogWarning($"Client {client.Id} Invalid header received");
                            continue;
                        }

                        // 3. 版本检查
                        if (!config.SupportedVersions.Contains((byte)header.Version))
                        {
                            logger.LogWarning($"Client {client.Id} Unsupported protocol version: {header.Version}");
                            continue;
                        }

                        // 4. 接收数据体
                        byte[] payloadBuffer = new byte[header.MessageLength];
                        if (!await ReadFullAsync(stream, payloadBuffer, (int)header.MessageLength))
                        {
                            logger.LogWarning($"Client {client.Id}  disconnected while reading payload");
                            return;
                        }

                        // 5. 组合完整数据包
                        byte[] fullPacket = new byte[8 + header.MessageLength];
                        Buffer.BlockCopy(headerBuffer, 0, fullPacket, 0, 8);
                        Buffer.BlockCopy(payloadBuffer, 0, fullPacket, 8, (int)header.MessageLength);

                        // 6. 解析数据包
                        var (success, packet, error) = ProtocolPacketWrapper.TryFromBytes(fullPacket);
                        if (!success)
                        {
                            logger.LogWarning($"Client {client.Id} Failed to parse packet: {error}");
                            continue;
                        }

                        client.UpdateActivity();
                        // 处理有效数据包
                        var message = new ClientMessage
                        {
                            Client = client,
                            Data = packet.Data,
                            ReceivedTime = DateTime.Now
                        };
                        switch (packet.Data.Priority)
                        {
                            case DataPriority.Low:
                                await _messagelowQueue.Writer.WriteAsync(message);
                                break;
                            case DataPriority.High:
                                await _messageHighQueue.Writer.WriteAsync(message);
                                break;
                            case DataPriority.Medium:
                                await _messageMediumQueue.Writer.WriteAsync(message);
                                break;
                        }

                        // 控制队列积压（可选）
                        if (_messagelowQueue.Reader.Count > MaxQueueSize)
                        {
                            logger.LogWarning($"Client {client.Id} message low queue growing");
                            // 可在此处实施背压策略，如暂停接收等
                        }
                        if (_messageMediumQueue.Reader.Count > MaxQueueSize)
                        {
                            logger.LogWarning($"Client {client.Id} message medium queue growing");
                            // 可在此处实施背压策略，如暂停接收等
                        }
                        if (_messageHighQueue.Reader.Count > MaxQueueSize)
                        {
                            logger.LogWarning($"Client {client.Id} message high queue growing");
                            // 可在此处实施背压策略，如暂停接收等
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError($"Client {client.Id} error: {ex.Message}");
                        break; // 发生异常时退出循环
                    }
                }
            }
            finally
            {
                DisconnectClient(client.Id);
            }
        }

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

        // 启动消费者（在适当位置调用，如服务启动时）
        public void StartProcessing()
        {
            // 根据CPU核心数设置消费者数量
            // Start high priority processors
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                logger.LogInformation($"Start high priority processors Seq {i}");
                _ = Task.Run(() => ProcessMessages(DataPriority.High));
            }

            // Start medium priority processors
            for (int i = 0; i < Environment.ProcessorCount / 2; i++)
            {
                logger.LogInformation($"Start medium priority processors Seq {i}");
                _ = Task.Run(() => ProcessMessages(DataPriority.Medium));
            }

            // Start low priority processor
            logger.LogInformation($"Start low priority processors");
            _ = Task.Run(() => ProcessMessages(DataPriority.Low));
        }
        private readonly Dictionary<DataPriority, SemaphoreSlim> _prioritySemaphores = new()
        {
            [DataPriority.High] = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2),
            [DataPriority.Medium] = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount),
            [DataPriority.Low] = new SemaphoreSlim(Environment.ProcessorCount / 2, Environment.ProcessorCount / 2)
        };
        // 新增消费者处理方法
        private async Task ProcessMessages(DataPriority priority)
        {
            var semaphore = _prioritySemaphores[priority];

            switch (priority)
            {
                case DataPriority.High:
                    await foreach (var message in _messageHighQueue.Reader.ReadAllAsync(_processingCts.Token))
                    {
                        if (message.Data.Priority != priority) continue;

                        await semaphore.WaitAsync(_processingCts.Token);
                        try
                        {
                            await ProcessMessageWithPriority(message, priority);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error processing {priority} priority message: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    break;
                case DataPriority.Medium:
                    await foreach (var message in _messageMediumQueue.Reader.ReadAllAsync(_processingCts.Token))
                    {
                        if (message.Data.Priority != priority) continue;

                        await semaphore.WaitAsync(_processingCts.Token);
                        try
                        {
                            await ProcessMessageWithPriority(message, priority);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error processing {priority} priority message: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    break;
                case DataPriority.Low:
                    await foreach (var message in _messagelowQueue.Reader.ReadAllAsync(_processingCts.Token))
                    {
                        if (message.Data.Priority != priority) continue;

                        await semaphore.WaitAsync(_processingCts.Token);
                        try
                        {
                            await ProcessMessageWithPriority(message, priority);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Error processing {priority} priority message: {ex.Message}");
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    break;
            }
        }
        private async Task ProcessMessageWithPriority(ClientMessage message, DataPriority priority)
        {
            var timeout = priority switch
            {
                DataPriority.High => TimeSpan.FromMilliseconds(100),
                DataPriority.Medium => TimeSpan.FromMilliseconds(500),
                _ => TimeSpan.FromSeconds(1)
            };

            using var cts = new CancellationTokenSource(timeout);

            try
            {
                switch (message.Data.InfoType)
                {
                    case InfoType.HeartBeat:
                        await HandleHeartbeat(message.Client, message.Data);
                        break;
                    case InfoType.File:
                        await HandleFileTransfer(message.Client, message.Data);
                        break;
                    case InfoType.Ack:
                        break;
                    default:
                        await HandleNormalMessage(message.Client, message.Data);
                        break;
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                logger.LogError($"{priority} priority message processing timeout");
            }
        }
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

        // 文件传输处理优化// 文件传输处理优化
        private readonly ConcurrentDictionary<string, FileTransferInfo> _activeTransfers = new();
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

        // 可选：定义文件完成事件
        public event Action<string> OnFileTransferCompleted;

        public void SetMonitorInterval(int interval)
        {
            lock (_lock)
            {
                _monitorInterval = interval;
                _trafficMonitorTimer.Change(interval, interval);
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            _processingCts.Cancel();
            _isRunning = false;
            _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _trafficMonitorTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _listener?.Dispose();

            foreach (var client in _clients.Values)
            {
                DisconnectClient(client.Id);
            }

            logger.LogCritical("Server stopped.");

            logger.Dispose();
        }
        // 创建协议配置（假设已在外部初始化）
        ProtocolConfiguration config = new ProtocolConfiguration
        {
            DataSerializer = new ProtobufSerializerAdapter(),
            ChecksumCalculator = new Crc16Calculator(),
            SupportedVersions = new byte[] { 0x01, 0x02 },
            MaxPacketSize = 128 * 1024 * 1024 // 与接收方配置一致
        };

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
                    new Protocol.ProtocolPacket()
                    {
                        Header = new Protocol.ProtocolHeader { Version = 0x01, Reserved = ByteString.CopyFrom(new byte[3]) },
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
                logger.LogError($"Client {client.Id} Unexpected error in SendData: {ex.Message}");
                return false;
            }
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

        private void DisconnectClient(int clientId)
        {

            if (_clients.TryRemove(clientId, out var client))
            {
                try
                {
                    if (client.Socket != null)
                    {
                        client.Socket.Shutdown(SocketShutdown.Both);
                    }
                    else if (client.SslStream != null)
                    {
                        client.SslStream?.Close();
                        client.Socket?.Dispose(); 
                    }

                    client.IsConnect = false;
                }
                catch { /* 忽略关闭异常 */ }

                client.Socket?.Dispose();

                var totalRec = client.BytesReceived + client.FileBytesReceived;
                var totalSent = client.BytesSent + client.FileBytesSent;

                string message = $"Client {clientId} disconnected. " +
                                $"Normal: Recv {Function.FormatBytes(client.BytesReceived)} Send {Function.FormatBytes(client.BytesSent)} | " +
                                $"File: Recv {Function.FormatBytes(client.FileBytesReceived)} Send {Function.FormatBytes(client.FileBytesSent)} | " +
                                $"Total: Recv {Function.FormatBytes(totalRec)} Send {Function.FormatBytes(totalSent)}";
                logger.LogWarning(message);
            }
        }


    }
}