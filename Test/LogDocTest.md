处理器	 AMD Ryzen 7 7735H with Radeon Graphics   3.20 GHz
RAM	     16.0 GB (15.2 GB 可用)
GPU      NVIDIA GeForce RTX 4060 Laptop GPU
ROM      SSD 1T

版本1: SHA-1: ae47ded142200f69b645e976adb7c42b449446c3
设计方案：
一、基础架构与单例模式
通过实现线程安全的单例，确保全局唯一日志实例
构建异步队列，支持通过配置开关异步写入文件，避免同步 I/O 阻塞主线程
日志级别控制：通过预编译指令控制日志级别开关，运行时通过ConsoleLogLevel和FileLogLevel配置不同目标的输出级别
二、日志处理流程
调用LogTrace/LogDebug等方法时，根据预编译指令决定是否执行日志逻辑
构造LogMessage对象，包含时间戳、线程信息、日志内容等，根据级别判断是否写入控制台或文件
控制台输出同步直接写入，根据日志级别设置控制台颜色
文件写入：异步模式下将消息加入队列，后台线程_logWriterTask循环消费队列并写入文件；同步模式下直接写入
队列保护：添加日志时设置 50ms 超时，队列满时记录错误到控制台并丢弃消息，避免无限阻塞
三、核心功能与限制
日志格式化：固定格式包含时间戳、级别、消息
异常处理：文件写入失败时重试（捕获IOException，文件被占用时等待 1 秒），其他异常直接记录到控制台
资源释放：Dispose方法取消后台任务，等待队列处理完成，释放CancellationTokenSource和队列资源

public class LoggerConfig
{
    public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Information;
    public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
    public string LogFilePath { get; set; } = "application.log";
    public bool EnableAsyncWriting { get; set; } = true;
}

| Method         | Mean       | Error       | StdDev    | Gen0      | Allocated |
|--------------- |-----------:|------------:|----------:|----------:|----------:|
| 单线程同步写入 |   127.3 ms |    44.96 ms |   2.46 ms |         - |  58.71 MB |
| 单线程异步写入 | 6,052.3 ms | 2,994.47 ms | 164.14 ms | 6000.0000 | 991.83 MB |

共510万条日志,运行时间1625.13,每条日志从生成到持久化的平均耗时约 615 微秒


版本2：SHA-1: 23c8b980e5843e1a18addc90302888da2d8f426f
优化方案：
一、日志处理架构升级
引入批量处理和定期刷新机制，减少文件 I/O 次数，提升吞吐量
分离控制台输出与文件写入逻辑，避免混合操作导致的性能损耗
二、性能与资源管理优化
引入对象池，复用日志消息数组，减少 GC 压力
使用缓存控制台颜色状态，避免多线程竞争的性能开销
采用异步文件流和预分配缓冲区提升写入效率
实现重试机制，自动恢复因文件被占用等临时故障导致的写入失败
分离文件流初始化与重置逻辑，支持动态更新日志路径
新增吞吐量监控任务，定期输出日志处理速率、队列长度等指标，便于调优和问题定位
三、功能扩展性提升
支持自定义日志格式
日志消息支持结构化数据和泛型状态对象，适配复杂业务场景
模板级日志级别过滤，可针对不同模板设置独立的日志级别阈值
支持自动捕获调用者信息，提升日志追溯能力
四、错误处理与稳定性
避免多线程下重复释放资源
确保在程序退出前强制刷新缓冲区，避免日志丢失
日志写入失败时自动记录错误日志到控制台，并重置文件流，防止单点故障导致整个日志系统瘫痪
五、代码结构与可维护性
将日志格式化、颜色处理、性能监控等逻辑拆分为独立方法，代码结构更清晰
确保多线程下模板操作的线程安全
配置灵活性
新增MaxQueueSize、EnableConsoleColor等配置项，适配不同环境需求

public class LoggerConfig
{
    public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Information;
    public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
    public string LogFilePath { get; set; } = "application.log";
    public bool EnableAsyncWriting { get; set; } = true;
    public int MaxQueueSize { get; set; } = 1_000_000;
    public int BatchSize { get; set; } = 10_000;
    public int FlushInterval { get; set; } = 500;
    public bool EnableConsoleColor { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
}

| Method                | Mean       | Error    | StdDev   | Gen0       | Gen1       | Gen2      | Allocated |
|----------------------:|---------:|---------:|-----------:|-----------:|----------:|----------:|
| 单线程_小消息         | 1,072.8 ms | 21.37 ms | 19.99 ms | 64500.0000 | 63300.0000 | 1300.0000 | 503.89 MB |
| 单线程_混合大小消息   | 1,105.8 ms | 21.80 ms | 25.95 ms | 67300.0000 | 64300.0000 | 1700.0000 | 521.35 MB |
| 多线程_小消息         |   792.9 ms | 15.46 ms | 21.17 ms | 63000.0000 | 63000.0000 |  400.0000 | 498.63 MB |
| 多线程_混合消息和级别 |   803.7 ms | 15.45 ms | 14.45 ms | 63800.0000 | 63500.0000 |  400.0000 |  502.8 MB |
| 极限并发_小消息       |   808.2 ms | 15.78 ms | 24.57 ms | 63500.0000 | 63500.0000 |  500.0000 |  498.7 MB |
| 异常日志测试          |   175.0 ms |  3.50 ms |  5.44 ms |  9200.0000 |  9200.0000 |  100.0000 |  72.58 MB | 
 
共510万条日志,运行时间1625.13秒（27分5秒）,每条日志从生成到持久化的平均耗时约 615 微秒


版本3：SHA-1: dff3b3029023c756565ec9b5a33b9f2fa889d565  
优化方案：
一、内存与文件写入优化
将LogMessage改为readonly struct，减少 GC 压力，提升值类型传递效率，尤其在高并发场景下降低内存分配频率
新增UseMemoryMappedFile配置，通过 Windows API直接操作内存映射区域，避免传统文件流的缓冲区拷贝开销，大幅提升写入速度
支持预分配文件大小和定期刷新，减少磁盘 I/O 次数，提升吞吐量。
利用多核 CPU 并行处理日志队列，提升整体处理能力
二、性能与资源管理增强
文件流写入使用固定大小缓冲区和锁，避免频繁的lock竞争
内存映射文件通过unsafe指针直接操作内存，减少托管代码与非托管代码的交互损耗
将控制台输出移至独立后台线程，通过异步收集日志消息，避免控制台 I/O 阻塞日志处理流程
使用Interlocked实现线程安全的计数器，精确统计吞吐量、队列长度等指标，监控数据更可靠
性能监控任务输出频率固定为 5 秒，减少诊断信息对主线程的影响。
三、代码健壮性与平台适配
提前校验文件写入权限和锁定状态，避免运行时异常
初始化文件写入器时增加详细错误日志（如 Win32 错误码、文件状态信息），提升故障排查效率
使用非托管资源，确保文件句柄和内存映射句柄的正确释放，避免内存泄漏
在Dispose方法中增加状态标记和强制刷新逻辑，确保程序退出前所有日志写入完成
修正调用中的句柄传递问题，确保与非托管代码交互的安全性。
显式设置句柄状态，避免意外引用无效句柄
四、功能扩展与灵活性
允许直接写入二进制数据（如预序列化的日志内容），减少字符串编码开销，适配高性能场景
模板管理使用确保多线程下模板操作的线程安全，支持动态添加 / 删除模板时的高效查找
泛型Log<T>方法直接处理state对象，通过formatter灵活转换为字符串，避免装箱拆箱，提升值类型日志的性能
五、代码结构与可维护性
将文件写入逻辑拆分，根据配置动态切换实现，代码结构更清晰
使用预分配的固定大小缓冲区，避免频繁的byte[]分配，提升格式化性能
通过Span<byte>和Memory<byte>优化内存拷贝，减少不必要的中间对象

public sealed class LoggerConfig
{
    public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Trace;
    public LogLevel FileLogLevel { get; set; } = LogLevel.Information;
    public string LogDirectory { get; set; } = "Logs"; 
    public string LogFileNameFormat => "Log_{0:yyyyMMdd}_{1:D3}.dat";
    public bool EnableAsyncWriting { get; set; } = true;
    public bool EnableConsoleWriting { get; set; } = false;
    public int MaxQueueSize { get; set; } = int.MaxValue;
    public int BatchSize { get; set; } = 10_000;
    public int FlushInterval { get; set; } = 500;
    public bool EnableConsoleColor { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 100;
    public int FileBufferSize { get; set; } = 64 * 1024;
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;
    public bool UseMemoryMappedFile { get; set; } = true;
    public long MemoryMappedFileSize { get; set; } = 1024 * 1024 * 1000; // 1000MB
    public long MemoryMappedThreadShould { get; set; } = 100 * 1024 * 1024;  // 100MB
}

| Method                | Mean       | Error       | StdDev    | Gen0       | Gen1      | Gen2      | Allocated  |
|----------------------:|-----------:|------------:|----------:|-----------:|----------:|----------:|-----------:|
| 多线程-小消息         |   358.4 ms |     5.87 ms |   1.52 ms |          - |         - |         - | 2167.78 MB |
| 异常日志测试          |   440.8 ms |    94.41 ms |  24.52 ms |   500.0000 |         - |         - | 1458.59 MB |
| 极限并发-小消息       |   453.3 ms |   921.28 ms | 239.25 ms |          - |         - |         - | 2238.67 MB |
| 单线程-小消息         | 1,047.6 ms | 1,420.05 ms | 219.76 ms | 11000.0000 | 4000.0000 | 1000.0000 | 3511.94 MB |
| 多线程-混合消息和级别 | 1,234.4 ms | 1,625.41 ms | 422.11 ms |  5000.0000 | 2000.0000 |         - | 1260.01 MB |
| 单线程-混合大小消息   | 2,112.0 ms | 3,302.80 ms | 857.73 ms |  1000.0000 |         - |         - |  469.21 MB |

共510万条日志，处理时间146.15秒（2分26秒），每条日志从生成到持久化的平均耗时约 28.66 微秒。
