using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using Server.Common.Log;
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
            int bytesRead;
            byte[] headerBuffer = new byte[8];
            byte[] payloadBuffer;
            int headerOffset = 0;
            int payloadOffset = 0;

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
                        // 第一阶段：接收协议头（带容错处理）
                        headerOffset = 0;
                        while (headerOffset < 8)
                        {
                            bytesRead = await stream.ReadAsync(headerBuffer, headerOffset, 8 - headerOffset);
                            if (bytesRead == 0) return; // 连接关闭
                            headerOffset += bytesRead;
                        }

                        // 使用Try模式解析协议头
                        if (!ProtocolHeader.TryFromBytes(headerBuffer, out ProtocolHeader header))
                        {
                            logger.LogError($"Invalid protocol header from {client.Id}");
                            continue; // 丢弃错误头，继续接收
                        }

                        // 版本兼容性检查（新增）
                        if (!config.SupportedVersions.Contains(header.Version))
                        {
                            logger.LogWarning($"Unsupported version {header.Version} from {client.Id}");
                            continue;
                        }

                        // 第二阶段：接收消息体（带长度校验）
                        payloadBuffer = new byte[header.MessageLength];
                        payloadOffset = 0;
                        while (payloadOffset < header.MessageLength)
                        {
                            bytesRead = await stream.ReadAsync(
                                payloadBuffer,
                                payloadOffset,
                                header.MessageLength - payloadOffset);

                            if (bytesRead == 0) return; // 连接关闭
                            payloadOffset += bytesRead;
                        }

                        // 第三阶段：完整解析（带校验和验证）
                        byte[] fullPacket = new byte[8 + header.MessageLength];
                        Array.Copy(headerBuffer, 0, fullPacket, 0, 8);
                        Array.Copy(payloadBuffer, 0, fullPacket, 8, header.MessageLength);

                        // 使用异步解析方法
                        var parseResult = await ProtocolPacket.TryFromBytesAsync(fullPacket, config);
                        if (!parseResult.Success)
                        {
                            logger.LogError($"Invalid protocol packet from {client.Id}");
                            continue; // 丢弃错误包，继续接收
                        }
                        client.UpdateActivity();
                        // 处理有效数据包
                        var message = new ClientMessage
                        {
                            Client = client,
                            Data = parseResult.Packet.Data,
                            ReceivedTime = DateTime.Now
                        };
                        switch (parseResult.Packet.Data.Priority)
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
            logger.LogInformation($"Client {client.Id} ACK: {data.SeqNum}");
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
                        await VerifyFileIntegrity(transferInfo, data.MD5Hash);

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

                        logger.LogInformation($"File {transferInfo.FileName} transfer completed successfully");

                        // 触发文件完成事件
                        OnFileTransferCompleted?.Invoke(transferInfo.FilePath);
                    }
                    else
                    {
                        logger.LogWarning($"Received FILE_COMPLETE for unknown file ID: {data.FileId}");
                    }
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
                    _activeTransfers[data.FileId] = transferInfo;
                }

                // 校验块MD5
                var chunkMd5 = CalculateChunkHash(data.ChunkData);
                if (chunkMd5 != data.ChunkMD5)
                {
                    logger.LogWarning($"Chunk {data.ChunkIndex} MD5 mismatch for file {data.FileId}");
                    return;
                }

                // 存储接收到的块
                transferInfo.ReceivedChunks[data.ChunkIndex] = data.ChunkData;

                // 如果所有块都已接收，组合文件
                if (transferInfo.ReceivedChunks.Count == transferInfo.TotalChunks)
                {
                    await CombineFileChunks(transferInfo);
                }

                // 发送接收确认
                var ack = new CommunicationData
                {
                    InfoType = InfoType.File,
                    AckNum = data.SeqNum,
                    Message = "FILE_ACK",
                    FileId = data.FileId,
                    ChunkIndex = data.ChunkIndex
                };

                client.AddFileSentBytes(MemoryCalculator.CalculateObjectSize(ack));
                await SendData(client, ack);

                logger.LogInformation($"Received chunk {data.ChunkIndex} of {data.TotalChunks} for file {data.FileId}");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error processing file transfer: {ex.Message}");
                throw;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task CombineFileChunks(FileTransferInfo transferInfo)
        {
            // 按顺序写入所有块
            using (var fs = new FileStream(transferInfo.FilePath, FileMode.Create, FileAccess.Write))
            {
                for (int i = 0; i < transferInfo.TotalChunks; i++)
                {
                    if (!transferInfo.ReceivedChunks.TryGetValue(i, out var chunkData))
                    {
                        throw new InvalidOperationException($"Missing chunk {i} for file {transferInfo.FileId}");
                    }

                    await fs.WriteAsync(chunkData, 0, chunkData.Length);
                }
            }

            logger.LogInformation($"All chunks received for file {transferInfo.FileId}, combined successfully");
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
            DataSerializer = new JsonSerializerAdapter(),
            ChecksumCalculator = new Crc16Calculator(),
            SupportedVersions = new byte[] { 0x01, 0x02 },
            MaxPacketSize = 2 * 1024 * 1024 // 与接收方配置一致
        };
        private async Task SendData(ClientConfig client, CommunicationData data)
        {
            // 创建协议数据包
            var packet = new ProtocolPacket(config)
            {
                Header = new ProtocolHeader { Version = ProtocolHeader.CurrentVersion },
                Data = data
            };

            // 生成符合协议的字节数组
            byte[] protocolBytes = packet.ToBytes();

            // 发送协议数据
            await client.Socket.SendAsync(protocolBytes, SocketFlags.None);
            client.AddSentBytes(protocolBytes.Length);
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