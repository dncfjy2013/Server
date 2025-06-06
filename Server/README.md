# ServerInstance 类开发文档
## 0. 概述
`ServerInstance`类是服务器核心功能的实现类，负责服务器的启动、停止、客户端连接管理、消息处理、流量监控等功能。该类通过多种网络协议（TCP、SSL、UDP、HTTP）监听客户端连接请求，并对不同类型的客户端消息进行优先级处理，同时支持文件传输、心跳检测等功能。

## 1. 文件框架
# 完整项目结构
├── Common/                 # 通用基础组件
│   ├── Extensions/         # 扩展方法
│   │   └── StringExtensions.cs
│   ├── Helpers/            # 通用帮助类
│   │   └── DateTimeHelper.cs
│   ├── Constants/          # 全局常量
│   │   └── SystemConstants.cs
│   ├── Enums/              # 枚举定义
│   │   └── ErrorCode.cs
│   ├── Exceptions/         # 自定义异常
│   │   └── ApiException.cs
│   └── Interfaces/         # 通用接口
│       └── IIdentifier.cs

├── Core/                   # 系统核心模块
│   ├── Network/            # 网络处理核心
│   │   ├── Middlewares/    # 中间件
│   │   │   └── ExceptionHandlerMiddleware.cs
│   │   ├── Routing/        # 自定义路由
│   │   ├── WebSockets/     # WebSocket处理
│   │   └── Http/           # HTTP协议增强
│   ├── DependencyInjection # 依赖注入配置
│   ├── Startup/            # 启动配置
│   ├── Services/           # 后台服务
│   ├── Lifecycle/          # 生命周期管理
│   ├── HealthChecks/       # 健康检查
│   └── Authentication/     # 认证授权核心

├── DataBase/               # 数据持久层
│   ├── Contexts/           # 数据库上下文
│   │   └── AppDbContext.cs
│   ├── Migrations/         # 迁移脚本
│   ├── Repositories/       # 仓储实现
│   │   └── UserRepository.cs
│   ├── Seeders/            # 数据初始化
│   ├── Models/             # 数据模型
│   ├── QueryFilters/       # 全局查询过滤器
│   └── TransactionManager/ # 事务管理

├── Entity/                 # 领域模型
│   ├── Domain/             # 领域对象
│   │   └── User.cs
│   ├── DTOs/               # 数据传输对象
│   │   ├── Requests/       # 请求模型
│   │   └── Responses/      # 响应模型
│   ├── Validators/         # 数据验证
│   │   └── UserValidator.cs
│   └── Mappings/           # 对象映射配置
│       └── AutoMapperProfile.cs

├── Logger/                 # 日志系统
│   ├── Providers/          # 日志提供程序
│   │   ├── FileLogger/     # 文件日志
│   │   ├── DbLogger/       # 数据库日志
│   │   └── ElasticLogger/  # ELK日志
│   ├── Middlewares/        # 日志中间件
│   ├── Filters/            # 日志过滤器
│   ├── Formatters/         # 日志格式化
│   └── LoggerHelper.cs     # 日志工具类

├── Process/                # 流程调度
│   ├── Schedulers/         # 任务调度器
│   │   ├── Quartz/         # Quartz实现
│   │   └── Hangfire/       # Hangfire实现
│   ├── Tasks/              # 后台任务
│   │   └── ReportGeneratorTask.cs
│   ├── Queues/             # 消息队列
│   │   ├── RabbitMQ/
│   │   └── Kafka/
│   ├── Workflows/          # 工作流引擎
│   └── BatchProcessing/    # 批处理

├── Utils/                  # 工具库
│   ├── Security/           # 安全相关
│   │   └── JwtHelper.cs
│   ├── Validation/         # 验证工具
│   ├── Serialization/      # 序列化
│   │   └── JsonSerializer.cs
│   ├── File/               # 文件操作
│   │   └── FileManager.cs
│   └── Network/            # 网络工具
│       └── HttpClientHelper.cs

├── Configs/                # 配置管理
│   ├── appsettings.json    # 主配置文件
│   ├── appsettings.Development.json
│   └── ConfigurationLoader.cs

├── API/                    # 接口层（可选）
│   ├── Controllers/        # WebAPI控制器
│   ├── GraphQL/            # GraphQL端点
│   └── gRPC/               # gRPC服务

├── Tests/                  # 测试套件
│   ├── UnitTests/          # 单元测试
│   ├── IntegrationTests/   # 集成测试
│   └── StressTests/        # 压力测试

├── Scripts/                # 辅助脚本
│   ├── Deployment/         # 部署脚本
│   ├── Database/           # 数据库脚本
│   └── CI_CD/              # 持续集成脚本

└── Docs/                   # 项目文档
    ├── API_Documentation/  # API文档
    ├── Architecture/       # 架构设计
    └── DevelopmentGuide.md # 开发指南

## 2. 功能模块
### 2.1 服务器初始化与配置
- **端口与证书配置**：通过构造函数传入普通端口号（`_port`）、SSL端口号（`_sslPort`）、UDP端口号（`_udpport`）以及SSL证书路径（`certPath`）进行初始化。若提供证书路径，则加载SSL证书（`_serverCert`），否则跳过SSL证书加载。
- **监控与日志配置**：初始化流量监控器（`_trafficMonitor`）、日志记录器（`_logger`）、心跳定时器（`_heartbeatTimer`）和流量监控定时器（`_trafficMonitorTimer`）。流量监控间隔（`_monitorInterval`）默认为5000毫秒，可通过`SetMonitorInterval`方法修改。

### 2.2 服务器启动与停止
- **启动**：调用`Start`方法启动服务器，该方法会：
    - 设置服务器运行状态（`_isRunning`）为`true`。
    - 启动心跳定时器和流量监控定时器（若启用监控）。
    - 启动普通TCP、SSL、UDP和HTTP端口的监听，并开始接受客户端连接。
    - 启动消息处理任务，包括接收和处理客户端消息、处理文件传输等。
- **停止**：调用`Stop`方法停止服务器，该方法会：
    - 取消所有异步操作的取消令牌源（`_cts`）。
    - 设置服务器运行状态为`false`，停止接受新连接。
    - 停止心跳定时器和流量监控定时器。
    - 断开所有客户端连接，清理相关资源（套接字、流等）。
    - 关闭各类监听器（`_listener`、`_sslListener`、`_httpListener`、`_udpListener`）。
    - 停止所有消息处理线程，释放日志记录器资源。

### 2.3 客户端连接管理
- **连接接受**：
    - `AcceptSocketClients`方法异步接受普通TCP客户端连接，为每个连接分配唯一客户端ID（`_nextClientId`），创建客户端配置对象（`ClientConfig`）并添加到客户端字典（`_clients`），然后启动客户端消息处理任务（`HandleClient`）。
    - `AcceptSslClients`方法异步接受SSL客户端连接，进行SSL握手验证，同样分配客户端ID、创建配置对象并启动消息处理任务。
    - `AcceptHttpClients`方法接受HTTP客户端连接，处理HTTP请求并返回响应。
    - `AcceptUdpClients`方法接收UDP数据，处理并回显消息给客户端。
- **连接断开**：`DisconnectClient`方法用于断开指定客户端连接，关闭相关网络连接，清理资源，统计流量数据并记录断开日志。同时，将断开的客户端添加到历史客户端字典（`_historyclients`）中。

### 2.4 消息处理
- **消息接收与入队**：`HandleClient`方法负责处理客户端连接，从客户端接收消息，解析消息头部和协议版本，校验消息完整性，然后根据消息类型和优先级将消息放入对应的消息队列（`_messageHighQueue`、`_messageMediumQueue`、`_messagelowQueue`）。
- **消息处理与分发**：`ProcessMessages`方法根据消息优先级从队列中取出消息进行处理，调用`ProcessMessageWithPriority`方法根据消息类型（心跳、文件传输、普通消息等）执行具体的处理逻辑。
- **消息发送**：
    - `SendDate`方法异步向客户端发送数据，确保数据完整发送，并处理可能出现的异常。
    - `SendToClient`方法用于主动向客户端发送消息，支持指定优先级，将消息放入对应的发送队列（`_outgoingHighMessages`、`_outgoingMedumMessages`、`_outgoingLowMessages`）。
    - `SendFileAsync`方法用于主动向客户端发送文件，将文件拆分为多个块并按高优先级发送，发送完成后发送文件完成标记。

### 2.5 文件传输处理
- **文件传输请求处理**：`HandleFileTransfer`方法处理文件传输请求，支持大文件分块传输、完整性校验和重传处理。接收文件分块数据时，进行MD5校验，存储分块数据，发送ACK确认消息，当所有分块接收完成后，合并文件分块并验证文件完整性。
- **文件合并与验证**：`CombineFileChunks`方法将接收到的文件分块合并为完整文件，`VerifyFileIntegrity`方法通过MD5哈希比对验证文件完整性。
- **文件路径生成**：`GetUniqueFilePath`方法生成唯一的文件路径，避免同名文件覆盖。

### 2.6 心跳检测
- **心跳消息处理**：`HandleHeartbeat`方法处理客户端发送的心跳消息，接收心跳消息后返回ACK确认消息，并更新客户端活动时间。
- **心跳状态检查**：`CheckHeartbeats`方法定期检查客户端心跳状态，若客户端在指定时间（`TimeoutSeconds`，默认为45秒）内未发送心跳消息，则视为心跳超时，断开客户端连接。

## 3. 类成员与方法详细说明
### 3.1 成员变量
| 成员变量名 | 类型 | 说明 |
| --- | --- | --- |
| `_port` | `int` | 服务器监听的普通端口号 |
| `_sslPort` | `int` | 服务器监听的SSL加密端口号 |
| `_udpport` | `int` | 服务器监听的UDP端口号 |
| `_serverCert` | `X509Certificate2` | 用于SSL连接的服务器证书对象 |
| `_trafficMonitor` | `TrafficMonitor` | 流量监控器实例 |
| `_logger` | `ILogger` | 日志记录器实例 |
| `_heartbeatTimer` | `Timer` | 心跳定时器 |
| `_trafficMonitorTimer` | `Timer` | 流量监控定时器 |
| `_monitorInterval` | `int` | 流量监控的时间间隔（单位：毫秒） |
| `_isRunning` | `bool` | 服务器运行状态标志 |
| `HeartbeatInterval` | `int` | 心跳检查的时间间隔（单位：毫秒，固定值为10000） |
| `_listener` | `Socket` | 用于普通TCP连接的套接字监听器 |
| `ListenMax` | `int` | 套接字监听器的最大连接队列长度（固定值为100） |
| `_sslListener` | `TcpListener` | 用于SSL加密连接的TCP监听器 |
| `_httpListener` | `HttpListener` | 用于HttpListener连接的监听器 |
| `_udpListener` | `UdpClient` | 用于UdpClient连接的监听器 |
| `_cts` | `CancellationTokenSource` | 用于取消异步操作的取消令牌源 |
| `_lock` | `object` | 用于线程安全的日志记录操作的锁对象 |
| `_clients` | `ConcurrentDictionary<uint, ClientConfig>` | 客户端连接字典（线程安全） |
| `_historyclients` | `ConcurrentDictionary<uint, ClientConfig>` | 历史客户端连接字典（线程安全） |
| `_nextClientId` | `uint` | 客户端ID生成器（原子递增） |
| `config` | `ProtocolConfiguration` | 协议全局配置 |
| `_ResumeMessages` | `ConcurrentDictionary<int, ConcurrentQueue<CommunicationData>>` | 存储待发送消息的队列字典 |
| `MaxQueueSize` | `int` | 消息队列的最大容量 |
| `_messageHighQueue` | `Channel<ClientMessage>` | 高优先级消息队列 |
| `_messageMediumQueue` | `Channel<ClientMessage>` | 中优先级消息队列 |
| `_messagelowQueue` | `Channel<ClientMessage>` | 低优先级消息队列 |
| `_incomingHighManager` | `IncomingMessageThreadManager` | 高优先级消息的线程管理器实例 |
| `_incomingMediumManager` | `IncomingMessageThreadManager` | 中优先级消息的线程管理器实例 |
| `_incomingLowManager` | `IncomingMessageThreadManager` | 低优先级消息的线程管理器实例 |
| `_prioritySemaphores` | `Dictionary<DataPriority, SemaphoreSlim>` | 不同优先级对应的信号量字典 |
| `_processingCts` | `CancellationTokenSource` | 用于取消消息处理任务的CancellationTokenSource |
| `_isReceiving` | `bool` | 控制是否继续接收新数据的标志 |
| `_isRealTimeTransferAllowed` | `bool` | 控制是否允许实时数据功能的标志 |
| `_outgoingHighMessages` | `Channel<ServerOutgoingMessage>` | 高优先级主动消息队列 |
| `_outgoingMedumMessages` | `Channel<ServerOutgoingMessage>` | 中优先级主动消息队列 |
| `_outgoingLowMessages` | `Channel<ServerOutgoingMessage>` | 低优先级主动消息队列 |
| `_retryPolicies` | `Dictionary<DataPriority, (int MaxRetries, TimeSpan Interval)>` | 不同优先级消息的重传策略字典 |
| `_highPriorityManager` | `OutgoingMessageThreadManager` | 高优先级消息处理线程管理器实例 |
| `_mediumPriorityManager` | `OutgoingMessageThreadManager` | 中优先级消息处理线程管理器实例 |
| `_lowPriorityManager` | `OutgoingMessageThreadManager` | 低优先级消息处理线程管理器实例 |
| `_activeTransfers` | `ConcurrentDictionary<string, FileTransferInfo>` | 正在进行的文件传输信息字典 |
| `_fileLock` | `SemaphoreSlim` | 用于文件操作的异步锁 |
| `OnFileTransferCompleted` | `Action<string>` | 文件传输完成事件 |
| `TimeoutSeconds` | `int` | 心跳超时时间（秒） |

### 3.2 方法说明
- **构造函数**：`ServerInstance(int port, int sslPort, int udpport, string certPath = null)`
    - 功能：初始化服务器实例，配置端口、证书、监控器、日志记录器等。
    - 参数：
        - `port`：普通端口号。
        - `sslPort`：SSL端口号。
        - `udpport`：UDP端口号。
        - `certPath`：SSL证书路径（可选）。
- **`SetMonitorInterval`方法**：`void SetMonitorInterval(int interval)`
    - 功能：设置流量监控的时间间隔。
    - 参数：`interval`：新的监控间隔（单位：毫秒）。
- **`Start`方法**：`void Start(bool enableMonitoring = false)`
    - 功能：启动服务器，开始监听客户端连接，启动各类定时器和消息处理任务。
    - 参数：`enableMonitoring`：是否启用流量监控（默认：`false`）。
- **`Stop`方法**：`void Stop()`
    - 功能：停止服务器，关闭所有连接，清理资源，停止消息处理线程。
- **`AcceptSslClients`方法**：`private async Task AcceptSslClients()`
    - 功能：异步接受SSL客户端连接，进行SSL握手验证，创建客户端配置对象并启动消息处理任务。
- **`AcceptSocketClients`方法**：`private async void AcceptSocketClients()`
    - 功能：异步接受普通TCP客户端连接，创建客户端配置对象并启动消息处理任务。
- **`AcceptHttpClients`方法**：`private async void AcceptHttpClients()`
    - 功能：接受HTTP客户端连接，处理HTTP请求并返回响应。
- **`AcceptUdpClients`方法**：`private async void AcceptUdpClients()`
    - 功能：接收UDP数据，处理并回显消息给客户端。
- **`DisconnectClient`方法**：`private void DisconnectClient(uint clientId)`
    - 功能：断开指定客户端连接，清理资源，统计流量数据并记录断开日志。
    - 参数：`clientId`：要断开连接的客户端ID。
- **`SendInfoDate`方法**：`private async Task<bool> SendInfoDate(ClientConfig client, CommunicationData data)`
    - 功能：根据消息类型向客户端发送数据，若目标客户端不在线则将消息添加到待发送队列。
    - 参数：
        - `client`：客户端配置对象。
        - `data`：要发送的通信数据。
    - 返回值：若数据发送成功或成功入队则返回`true`，否则返回`false`。
- **`SendPendingMessages`方法**：`private async Task SendPendingMessages(ClientConfig client, ConcurrentQueue<CommunicationData> queue)`
    - 功能：向客户端发送待发送队列中的消息。
    - 参数：
        - `client`：客户端配置对象。
        - `queue`：待发送消息队列。
- **`ReadFullAsync`方法**：`private async Task<bool> ReadFullAsync(Stream stream, byte[] buffer, int count)`
    - 功能：从流中异步读取指定数量的字节到缓冲区。
    - 参数：
        - `stream`：要读取数据的流。
        - `buffer`：用于存储读取数据的缓冲区。
        - `count`：需要读取的字节数。
    - 返回值：若成功读取指定数量的字节则返回`true`，否则返回`false`。
- **`SendDate`方法**：`private async Task<bool> SendDate(ClientConfig client, CommunicationData data)`
    - 功能：异步向客户端发送数据，确保数据完整发送，并处理可能出现的异常。
    - 参数：
        - `client`：客户端配置对象。
        - `data`：要发送的通信数据。
    - 返回值：若数据发送成功则返回`true`，否则返回`false`。
- **`StartProcessing`方法**：`public void StartProcessing()`
    - 功能：启动消息处理消费者，根据CPU核心数为不同优先级的消息队列启动相应数量的消费者任务。
- **`ProcessMessages`方法**：`public async Task ProcessMessages(DataPriority priority)`
    - 功能：处理不同优先级的客户端消息，根据优先级从队列中取出消息进行处理。
    - 参数：`priority`：消息优先级。
- **`ProcessPriorityMessages`方法**：`private async Task ProcessPriorityMessages(DataPriority priority, ChannelReader<ClientMessage> reader, SemaphoreSlim semaphore)`
    - 功能：处理特定优先级消息的通用逻辑，包括等待信号量、处理消息核心逻辑和释放信号量。
    - 参数：
        - `priority`：消息优先级。
        - `reader`：通道读取器。
        - `semaphore`：对应优先级的信号量。
- **`ProcessMessageWithPriority`方法**：`private async Task ProcessMessageWithPriority(ClientMessage message, DataPriority priority)`
    - 功能：按优先级处理客户端消息，根据消息类型执行具体的处理逻辑，并设置处理超时时间。
    - 参数：
        - `message`：待处理的客户端消息。
        - `priority`：消息优先级。
- **`HandleClient`方法**：`private async Task HandleClient(ClientConfig client)`
    - 功能：处理客户端连接，接收消息，解析消息并根据消息类型和优先级将消息入队。
    - 参数：`client`：客户端配置对象。
- **`MonitorQueueBackpressure`方法**：`private async Task MonitorQueueBackpressure(ClientConfig client, DataPriority priority, int messageSize)`
    - 功能：监控队列积压并执行背压策略，根据优先级和队列积压情况暂停接收新消息。
    - 参数：
        - `client`：客户端配置对象。
        - `priority`：消息优先级。
        - `messageSize`：消息大小。
- **`ImplementBackpressure`方法**：`private async Task ImplementBackpressure(ClientConfig client, TimeSpan delay)`
    - 功能：执行背压策略，暂停接收新消息一段时间。
    - 参数：
        - `client`：客户端配置对象。
        - `delay`：暂停接收的时间间隔。
- **`IsVideoOrVoiceRequest`方法**：`private bool IsVideoOrVoiceRequest(CommunicationData data)`
    - 功能：判断是否为视频或语音通信请求。
    - 参数：`data`：通信数据。
    - 返回值：若是视频或语音通信请求则返回`true`，否则返回`false`。
- **`EstablishDirectConnection`方法**：`private async Task EstablishDirectConnection(ClientConfig client1, ClientConfig client2)`
    - 功能：建立两个客户端之间的直接连接，实现双向数据传输。
    - 参数：
        - `client1`：客户端1配置对象。
        - `client2`：客户端2配置对象。
- **`CopyStreamAsync`方法**：`private async Task CopyStreamAsync(Stream source, Stream destination)`
    - 功能：异步复制流，将数据从源流复制到目标流。
    - 参数：
        - `source`：源流。
        - `destination`：目标流。
- **`ModifyRealTimeTransfer`方法**：`public void ModifyRealTimeTransfer(bool value)`
    - 功能：修改是否允许实时数据传输的标志。
    - 参数：`value`：新的实时数据传输状态。
- **`HandleHttpClient`方法**：`private async Task HandleHttpClient(HttpListenerContext context)`
    - 功能：处理HTTP客户端请求，读取请求内容，生成响应并返回给客户端。
    - 参数：`context`：HTTP请求上下文。