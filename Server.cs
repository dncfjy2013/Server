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
        private readonly ConcurrentDictionary<int, DateTime> _lastHeartbeatTimes = new();
        private readonly object _lock = new(); // 用于线程安全日志记录
        Logger logger = Logger.GetInstance();
        private readonly TrafficMonitor _trafficMonitor;
        // 新增文件传输相关字段
        private readonly ConcurrentDictionary<string, FileTransferInfo> _activeTransfers = new();
        private SemaphoreSlim _fileLock = new SemaphoreSlim(1, 1); // 异步锁
        private Channel<ClientMessage> _messageQueue = Channel.CreateUnbounded<ClientMessage>();
        private readonly CancellationTokenSource _processingCts = new();
        private readonly int MaxQueueSize = int.MaxValue;

        public Server(int port, int sslPort, string certPath = null)
        {
            _port = port;
            _sslPort = sslPort;

            // 加载SSL证书
            if (!string.IsNullOrEmpty(certPath))
            {
                _serverCert = new X509Certificate2(certPath);
            }

            // 初始化流量监控器（关键修改）
            _trafficMonitor = new TrafficMonitor(_clients, _monitorInterval);

            _heartbeatTimer = new Timer(_ => CheckHeartbeats(), null, Timeout.Infinite, Timeout.Infinite);
            _trafficMonitorTimer = new Timer(_ => _trafficMonitor.Monitor(), null, Timeout.Infinite, Timeout.Infinite);
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
            logger.Log(LogLevel.Info, $"Socket started on port {_port} with monitoring {(enableMonitoring ? "enabled" : "disabled")}.");

            // 启动SSL端口监听
            if (_serverCert != null)
            {
                _sslListener = new TcpListener(IPAddress.Any, _sslPort);
                _sslListener.Start();
                logger.Log(LogLevel.Info, $"SSL started on port {_port} with monitoring {(enableMonitoring ? "enabled" : "disabled")}.");
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

                    logger.Log(LogLevel.Info, $"SSL Client {clientId} connected: {sslClient.Client.RemoteEndPoint}");
                    _ = HandleClient(client);
                }
                catch (Exception ex)
                {
                    logger.Log(LogLevel.Error, $"SSL accept error: {ex.Message}");
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

                    logger.Log(LogLevel.Info, $"Socket Client {clientId} connected: {clientSocket.RemoteEndPoint}");

                    _ = HandleClient(client);
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // 正常关闭时忽略异常
                }
                catch (Exception ex)
                {
                    logger.Log(LogLevel.Error, $"Socket Accept error: {ex.Message}");
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
                            logger.Log(LogLevel.Error, $"Invalid protocol header from {client.Id}");
                            continue; // 丢弃错误头，继续接收
                        }

                        // 版本兼容性检查（新增）
                        if (!config.SupportedVersions.Contains(header.Version))
                        {
                            logger.Log(LogLevel.Warning, $"Unsupported version {header.Version} from {client.Id}");
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
                            logger.Log(LogLevel.Error, $"Invalid protocol packet from {client.Id}");
                            continue; // 丢弃错误包，继续接收
                        }

                        // 处理有效数据包
                        var message = new ClientMessage
                        {
                            Client = client,
                            Data = parseResult.Packet.Data,
                            ReceivedTime = DateTime.Now
                        };
                        await _messageQueue.Writer.WriteAsync(message);

                        // 控制队列积压（可选）
                        if (_messageQueue.Reader.Count > MaxQueueSize)
                        {
                            logger.Log(LogLevel.Warning, $"Client {client.Id} message queue growing");
                            // 可在此处实施背压策略，如暂停接收等
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log(LogLevel.Error, $"Client {client.Id} error: {ex}");
                        break; // 发生异常时退出循环
                    }
                }
            }
            finally
            {
                DisconnectClient(client.Id);
            }
        }

        // 新增消费者处理方法
        private async Task ProcessMessages(CancellationToken token)
        {
            try
            {
                await foreach (var message in _messageQueue.Reader.ReadAllAsync(token))
                {
                    try
                    {
                        var data = message.Data;

                        if (data == null) continue;

                        // 更新心跳
                        _lastHeartbeatTimes[message.Client.Id] = message.ReceivedTime;
                        message.Client.UpdateHeartbeat();

                        // 处理不同类型消息
                        switch (data.InfoType)
                        {
                            case InfoType.HeartBeat:
                                await HandleHeartbeat(message.Client, data);
                                break;
                            case InfoType.File:
                                await HandleFileTransfer(message.Client, data);
                                break;
                            default:
                                await HandleNormalMessage(message.Client, data);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Log(LogLevel.Error, $"Processing error: {ex.Message}");
                        // 可选择将失败消息重新入队或记录日志
                    }
                }
            }
            finally
            {
                _processingCts.Cancel();
            }
        }

        // 启动消费者（在适当位置调用，如服务启动时）
        public void StartProcessing()
        {
            // 根据CPU核心数设置消费者数量
            int processorCount = Environment.ProcessorCount;
            for (int i = 0; i < processorCount; i++)
            {
                _ = Task.Run(() => ProcessMessages(_processingCts.Token), _processingCts.Token);
            }
        }

        // 心跳处理优化
        private async Task HandleHeartbeat(ClientConfig client, CommunicationData data)
        {
            var ack = new CommunicationData
            {
                InfoType = InfoType.HeartBeat,
                Message = "ACK",
                AckNum = data.SeqNum
            };
            logger.Log(LogLevel.Info, $"Client {client.Id} heartbeat");
            await SendData(client, ack);
        }

        // 普通消息处理
        private async Task HandleNormalMessage(ClientConfig client, CommunicationData data)
        {
            client.AddReceivedBytes(data.Message.Length + 4);
            var ack = new CommunicationData
            {
                InfoType = InfoType.Normal,
                AckNum = data.SeqNum,
                Message = "ACK"
            };
            await SendData(client, ack);
            logger.Log(LogLevel.Info, $"Client {client.Id} ACK: {data.SeqNum}");
        }

        // 文件传输处理优化// 文件传输处理优化
        private async Task HandleFileTransfer(ClientConfig client, CommunicationData data)
        {
            await _fileLock.WaitAsync();
            try
            {
                // 处理文件传输完成消息
                if (data.Message == "FILE_COMPLETE")
                {
                    if (_activeTransfers.TryRemove(data.FileId, out var transferInfo1))
                    {
                        // 发送完成确认（可选，根据协议需求）
                        var completionAck = new CommunicationData
                        {
                            InfoType = InfoType.File,
                            AckNum = data.SeqNum,
                            Message = "FILE_COMPLETE_ACK",
                            FileId = data.FileId
                        };
                        await SendData(client, completionAck);

                        logger.Log(LogLevel.Info, $"File {transferInfo1.FileName} transfer complete，Clean log");

                        // 可选：触发文件完成事件通知其他组件
                        OnFileTransferCompleted?.Invoke(transferInfo1.FilePath);
                    }
                    else
                    {
                        logger.Log(LogLevel.Warning, $"Receive FILE_COMPLETE But {data.FileId} not exit");
                    }
                    return;
                }

                // 常规文件块处理逻辑
                if (!_activeTransfers.TryGetValue(data.FileId, out var transferInfo))
                {
                    transferInfo = new FileTransferInfo
                    {
                        TotalChunks = data.TotalChunks,
                        FileName = data.FileName,
                        ReceivedChunks = new HashSet<int>(),
                        FilePath = Path.Combine(client.FilePath, data.FileName)
                    };
                    _activeTransfers[data.FileId] = transferInfo;
                    Directory.CreateDirectory(Path.GetDirectoryName(transferInfo.FilePath));
                }

                using (var fs = new FileStream(transferInfo.FilePath, FileMode.OpenOrCreate, FileAccess.Write))
                {
                    foreach (var chunk in data.FileChunks)
                    {
                        if (transferInfo.ReceivedChunks.Contains(chunk.Index)) continue;

                        fs.Seek(chunk.Index * Constants.ChunkSize, SeekOrigin.Begin);
                        await fs.WriteAsync(chunk.Data, 0, chunk.Data.Length);
                        transferInfo.ReceivedChunks.Add(chunk.Index);
                    }
                }

                _activeTransfers[data.FileId] = transferInfo;

                // 发送接收确认
                var ack = new CommunicationData
                {
                    InfoType = InfoType.File,
                    AckNum = data.SeqNum,
                    Message = "FILE_ACK",
                    ReceivedChunks = transferInfo.ReceivedChunks.ToList()
                };
                await SendData(client, ack);

                // 检查是否完成传输
                if (transferInfo.ReceivedChunks.Count == transferInfo.TotalChunks)
                {
                    using (var md5 = MD5.Create())
                    using (var stream = File.OpenRead(transferInfo.FilePath))
                    {
                        byte[] checksum = md5.ComputeHash(stream);
                        if (BitConverter.ToString(checksum).Replace("-", "") != data.MD5Hash)
                        {
                            throw new InvalidOperationException("MD5校验失败");
                        }
                    }

                    logger.Log(LogLevel.Info, $"文件 {data.FileName} 接收完成，MD5校验通过");
                }
            }
            finally
            {
                _fileLock.Release();
            }
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

            logger.Log(LogLevel.Info, "Server stopped.");

            logger.Stop();
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

            foreach (var clientId in _lastHeartbeatTimes.Keys)
            {
                if (now - _lastHeartbeatTimes[clientId] > timeout)
                {
                    if (_clients.TryGetValue(clientId, out var client))
                    {
                        logger.Log(LogLevel.Warning, $"Client {clientId} heartbeat timeout");
                        DisconnectClient(clientId);
                        _lastHeartbeatTimes.TryRemove(clientId, out _);
                    }
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
                logger.Log(LogLevel.Info, message);
            }
        }
    }
}