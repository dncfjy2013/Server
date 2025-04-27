using Microsoft.VisualBasic;
using Server.Client;
using Server.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Server.Extend
{
    public class TrafficMonitor
    {
        // 用于存储客户端配置的并发字典，键为客户端的唯一标识（整数类型），值为客户端配置对象
        // 并发字典支持多线程安全访问，适合在高并发场景下存储和管理客户端信息
        private readonly ConcurrentDictionary<int, ClientConfig> _clients;

        // 用于线程同步的锁对象，在需要对共享资源进行线程安全操作时使用
        // 通过使用锁，可以确保同一时间只有一个线程能够访问被保护的代码块
        private readonly object _lock = new();

        // 监控间隔时间（单位可能为毫秒等，具体取决于业务逻辑）
        // 该值决定了监控任务执行的时间间隔，例如每隔一定时间检查一次客户端的状态等
        private readonly int _monitorInterval;

        // 用于统计总流量的秒表，可记录从开始到结束的时间间隔
        // 可用于计算一段时间内的总流量，辅助进行流量监控和性能分析
        private readonly Stopwatch _totalTrafficWatch = new();

        // 用于存储每个客户端的实时流量统计信息的并发字典
        // 键为客户端的唯一标识（整数类型）
        // 值是一个包含多个流量统计信息的元组，包括：
        // - Received: 已接收的普通数据量
        // - Sent: 已发送的普通数据量
        // - FileReceived: 已接收的文件数据量
        // - FileSent: 已发送的文件数据量
        // - ReceivedCount: 普通数据接收次数
        // - SentCount: 普通数据发送次数
        // - FileReceivedCount: 文件数据接收次数
        // - FileSentCount: 文件数据发送次数
        // - ConnectionWatch: 用于记录客户端连接时长的秒表
        private readonly ConcurrentDictionary<int, (long Received, long Sent, long FileReceived, long FileSent,
            long ReceivedCount, long SentCount, long FileReceivedCount, long FileSentCount, Stopwatch ConnectionWatch)> _clientTrafficStats = new();

        // 用于存储每个客户端的历史流量统计信息的并发字典
        // 结构与 _clientTrafficStats 类似，但存储的是历史数据，可用于后续的数据分析和报告
        private readonly ConcurrentDictionary<int, (long Received, long Sent, long FileReceived, long FileSent,
            long ReceivedCount, long SentCount, long FileReceivedCount, long FileSentCount, Stopwatch ConnectionWatch)> _clientHistoryTrafficStats = new();

        // 采样率，用于控制数据采样的频率
        // 例如，值为 1 表示每执行一次就进行采样，值为 10 表示每 10 次执行进行一次采样
        private readonly int _samplingRate = 1;

        // 采样计数器，用于记录执行次数，配合采样率进行数据采样
        // 每执行一次相关操作，该计数器加 1，当达到采样率的倍数时进行数据采样
        private int _sampleCounter;

        // 流量监控功能的启用标志
        // 当该值为 true 时，开启流量监控功能；为 false 时，关闭流量监控功能
        private bool _enableTrafficMonitoring = false;

        // 日志记录器实例，用于记录程序运行过程中的各种信息，如错误信息、调试信息等
        // 可帮助开发者进行程序调试和问题排查
        private Logger logger = new Logger();

        /// <summary>
        /// 流量监控器的构造函数，用于初始化监控器并启动流量监控
        /// </summary>
        /// <param name="clients">存储客户端配置的并发字典</param>
        /// <param name="monitorInterval">监控的时间间隔</param>
        public TrafficMonitor(ConcurrentDictionary<int, ClientConfig> clients, int monitorInterval)
        {
            // 记录 Trace 日志，表明开始执行构造函数
            logger.LogTrace("Starting the constructor of TrafficMonitor.");

            try
            {
                // 将传入的客户端配置字典赋值给类的私有字段
                _clients = clients;
                // 记录 Debug 日志，显示客户端配置字典已成功赋值
                logger.LogDebug("Clients configuration dictionary has been assigned.");

                // 将传入的监控时间间隔赋值给类的私有字段
                _monitorInterval = monitorInterval;
                // 记录 Debug 日志，显示监控时间间隔已成功赋值
                logger.LogDebug($"Monitor interval has been set to {monitorInterval}.");

                // 启动总流量统计的秒表
                _totalTrafficWatch.Start();
                // 记录 Info 日志，表明总流量统计秒表已启动
                logger.LogInformation("Total traffic stopwatch has been started.");

                // 遍历所有客户端配置
                foreach (var client in _clients.Values)
                {
                    // 将客户端的初始流量统计信息存储到客户端实时流量统计字典中
                    _clientTrafficStats[client.Id] = (client.BytesReceived, client.BytesSent, client.FileBytesReceived, client.FileBytesSent, client.ReceiveCount, client.SendCount, client.ReceiveFileCount, client.SendFileCount, client.ConnectionWatch);
                    // 记录 Debug 日志，显示已为特定客户端初始化流量统计信息
                    logger.LogDebug($"Initialized traffic statistics for client with ID {client.Id}.");
                }

                // 记录 Info 日志，表明所有客户端的流量统计信息已初始化完成
                logger.LogInformation("Traffic statistics for all clients have been initialized.");
            }
            catch (Exception ex)
            {
                // 若在构造函数执行过程中出现异常，记录 Error 日志，显示异常信息
                logger.LogError($"An error occurred during the initialization of TrafficMonitor: {ex.Message} {ex}");
            }

            // 记录 Trace 日志，表明构造函数执行结束
            logger.LogTrace("Constructor of TrafficMonitor has been completed.");
        }

        /// <summary>
        /// 修改流量监控功能的启用状态
        /// </summary>
        /// <param name="enable">一个布尔值，true 表示启用流量监控，false 表示禁用流量监控</param>
        public void ModifyEnable(bool enable)
        {
            // 记录 Trace 日志，表明进入 ModifyEnable 方法
            logger.LogTrace($"Entering ModifyEnable method with enable value: {enable}");

            try
            {
                // 将传入的启用状态赋值给类的私有字段，以控制流量监控功能的开启或关闭
                _enableTrafficMonitoring = enable;

                // 记录 Debug 日志，显示流量监控功能的新启用状态
                logger.LogDebug($"Traffic monitoring is now set to {(_enableTrafficMonitoring ? "enabled" : "disabled")}");

                // 记录 Info 日志，表明流量监控功能的启用状态已成功修改
                logger.LogInformation($"Traffic monitoring enable status has been modified to {(_enableTrafficMonitoring ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                // 若在修改启用状态过程中出现异常，记录 Error 日志，显示异常信息
                logger.LogError($"An error occurred while modifying the traffic monitoring enable status: {ex.Message} {ex}");
            }

            // 记录 Trace 日志，表明 ModifyEnable 方法执行结束
            logger.LogTrace("Exiting ModifyEnable method");
        }

        /// <summary>
        /// 流量监控主方法（按采样率统计客户端流量数据）
        /// </summary>
        public void Monitor()
        {
            // 若未启用流量监控，直接返回
            if (!_enableTrafficMonitoring)
            {
                logger.LogTrace("Traffic monitoring is disabled, skipping monitor");
                return;
            }

            // 采样计数器递增
            _sampleCounter++;
            // 采样机制：仅当计数器是采样率的整数倍时执行统计
            if (_sampleCounter % _samplingRate != 0)
            {
                logger.LogTrace($"Sampling counter {_sampleCounter} not reached threshold {_samplingRate}, skipping");
                return;
            }

            logger.LogDebug($"Entering monitor with sample counter {_sampleCounter}");

            // 加锁确保线程安全（操作共享的客户端统计数据）
            lock (_lock)
            {
                try
                {
                    #region 同步新增客户端流量统计
                    logger.LogTrace("Syncing new client entries");
                    foreach (var client in _clients.Values)
                    {
                        if (!_clientTrafficStats.ContainsKey(client.Id))
                        {
                            // 初始化新客户端的流量统计
                            _clientTrafficStats[client.Id] = (
                                client.BytesReceived,
                                client.BytesSent,
                                client.FileBytesReceived,
                                client.FileBytesSent,
                                client.ReceiveCount,
                                client.SendCount,
                                client.ReceiveFileCount,
                                client.SendFileCount,
                                client.ConnectionWatch
                            );
                            logger.LogInformation($"New client {client.Id} traffic stats initialized");
                        }
                    }
                    #endregion

                    #region 清理已断开客户端的过时数据
                    logger.LogTrace("Cleaning up stale client entries");
                    var activeClientIds = _clients.Keys;
                    var staleEntries = _clientTrafficStats.Keys.Where(k => !activeClientIds.Contains(k)).ToList();

                    foreach (var id in staleEntries)
                    {
                        if (_clientTrafficStats.TryRemove(id, out var oldStats))
                        {
                            // 移动旧数据到历史统计
                            _clientHistoryTrafficStats.TryAdd(id, oldStats);
                            logger.LogWarning($"Client {id} disconnected, moved stats to history");
                        }
                    }
                    #endregion

                    #region 计算单个客户端流量差异
                    logger.LogTrace("Calculating per-client traffic deltas");
                    foreach (var client in _clients.Values)
                    {
                        if (_clientTrafficStats.TryGetValue(client.Id, out var stats))
                        {
                            // 解构统计元组
                            var (prevRec, prevSent, prevFileRec, prevFileSent,
                                prevRecCount, prevSentCount, prevFileRecCount, prevFileSentCount, prevConnWatch) = stats;

                            // 计算流量差值（当前值 - 上次统计值）
                            var recDiff = client.BytesReceived - prevRec;
                            var sentDiff = client.BytesSent - prevSent;
                            var fileRecDiff = client.FileBytesReceived - prevFileRec;
                            var fileSentDiff = client.FileBytesSent - prevFileSent;

                            // 计算计数差值
                            var recCountDiff = client.ReceiveCount - prevRecCount;
                            var sentCountDiff = client.SendCount - prevSentCount;
                            var fileRecCountDiff = client.ReceiveFileCount - prevFileRecCount;
                            var fileSentCountDiff = client.SendFileCount - prevFileSentCount;

                            // 计算连接时长变化
                            var connTimeDiff = (client.ConnectionWatch.Elapsed - prevConnWatch.Elapsed).Duration();

                            // 构建详细流量日志消息
                            var message = $@"[Client {client.Id}] Traffic Summary:
                                            Normal Data: Recv {Function.FormatBytes(recDiff)} ({recCountDiff} times), Sent {Function.FormatBytes(sentDiff)} ({sentCountDiff} times)
                                            File Data: Recv {Function.FormatBytes(fileRecDiff)} ({fileRecCountDiff} files), Sent {Function.FormatBytes(fileSentDiff)} ({fileSentCountDiff} files)
                                            Total: Recv {Function.FormatBytes(recDiff + fileRecDiff)} | Sent {Function.FormatBytes(sentDiff + fileSentDiff)}
                                            Connection Time: {client.ConnectionWatch.Elapsed:mm\\:ss} (Delta: {connTimeDiff:mm\\:ss})
                                            History Total: Recv {Function.FormatBytes(client.BytesReceived)} | Sent {Function.FormatBytes(client.BytesSent + client.FileBytesSent)}";

                            // 记录 Debug 日志（详细流量数据）
                            logger.LogDebug(message);

                            // 更新为当前统计值（供下次计算差值）
                            _clientTrafficStats[client.Id] = (
                                client.BytesReceived,
                                client.BytesSent,
                                client.FileBytesReceived,
                                client.FileBytesSent,
                                client.ReceiveCount,
                                client.SendCount,
                                client.ReceiveFileCount,
                                client.SendFileCount,
                                client.ConnectionWatch
                            );
                        }
                    }
                    #endregion

                    #region 计算全局流量统计
                    logger.LogTrace("Calculating global traffic statistics");
                    var globalRecv = _clients.Values.Sum(c => c.BytesReceived);
                    var globalSent = _clients.Values.Sum(c => c.BytesSent);
                    var globalFileRecv = _clients.Values.Sum(c => c.FileBytesReceived);
                    var globalFileSent = _clients.Values.Sum(c => c.FileBytesSent);

                    var globalRecvCount = _clients.Values.Sum(c => c.ReceiveCount);
                    var globalSentCount = _clients.Values.Sum(c => c.SendCount);
                    var globalFileRecvCount = _clients.Values.Sum(c => c.ReceiveFileCount);
                    var globalFileSentCount = _clients.Values.Sum(c => c.SendFileCount);

                    if (globalRecv > 0 || globalSent > 0 || globalFileRecv > 0 || globalFileSent > 0)
                    {
                        var totalMessage = $@"Global Traffic Summary:
                                            Normal Data: Recv {Function.FormatBytes(globalRecv)} ({globalRecvCount} times), Sent {Function.FormatBytes(globalSent)} ({globalSentCount} times)
                                            File Data: Recv {Function.FormatBytes(globalFileRecv)} ({globalFileRecvCount} files), Sent {Function.FormatBytes(globalFileSent)} ({globalFileSentCount} files)
                                            Total: Recv {Function.FormatBytes(globalRecv + globalFileRecv)} | Sent {Function.FormatBytes(globalSent + globalFileSent)}";

                        // 记录 Info 日志（全局流量概览）
                        logger.LogInformation(totalMessage);
                    }
                    #endregion

                    #region 每日历史数据清理
                    logger.LogTrace("Checking for daily history cleanup");
                    var currentTime = DateTime.Now;
                    if (currentTime.Date > DateTime.Today) // 跨天后执行清理
                    {
                        _clientHistoryTrafficStats.Clear();
                        logger.LogInformation("Daily traffic history cleared");
                    }
                    #endregion
                }
                catch (Exception ex)
                {
                    // 记录致命错误（如锁竞争、数据解析异常）
                    logger.LogError($"Critical error in traffic monitor: {ex.Message} {ex}");
                }
            }
        }

    }
    /// <summary>
    /// 内存计算工具类（计算对象实例的内存占用大小）
    /// </summary>
    public static class MemoryCalculator
    {
        /// <summary>
        /// 计算指定对象的内存占用大小（基于反射分析对象字段）
        /// </summary>
        /// <typeparam name="T">对象类型（需为引用类型）</typeparam>
        /// <param name="obj">待计算的对象实例</param>
        /// <returns>对象的内存占用大小（字节）</returns>
        public static long CalculateObjectSize<T>(T obj) where T : class
        {
            if (obj == null)
            {
                // 记录 Error 日志：传入空对象
                Logger.Instance.LogError("Cannot calculate size of null object");
                return 0;
            }

            long size = 0;
            Type type = typeof(T);

            // 记录 Trace 日志：开始计算对象内存
            Logger.Instance.LogTrace($"Calculating memory size for object of type {type.FullName}");

            try
            {
                // 对象头开销（同步块索引 + 类型指针，固定为 16 字节）
                size += 16;
                Logger.Instance.LogDebug($"Added object header size: 16 bytes");

                // 遍历对象所有实例字段（包括公有和私有字段）
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    try
                    {
                        object value = field.GetValue(obj);
                        Type fieldType = field.FieldType;
                        string fieldName = field.Name;

                        // 记录 Debug 日志：处理当前字段
                        Logger.Instance.LogDebug($"Processing field: {fieldName} ({fieldType.Name})");

                        if (fieldType.IsPrimitive)
                        {
                            // 基础类型（如 int、double 等）直接计算 Marshal 大小
                            long primitiveSize = Marshal.SizeOf(fieldType);
                            size += primitiveSize;
                            Logger.Instance.LogTrace($"Primitive type {fieldType.Name}, size: {primitiveSize} bytes");
                        }
                        else if (fieldType.IsEnum)
                        {
                            // 枚举类型按基础类型大小计算（如 int、long 等）
                            Type enumUnderlyingType = Enum.GetUnderlyingType(fieldType);
                            long enumSize = Marshal.SizeOf(enumUnderlyingType);
                            size += enumSize;
                            Logger.Instance.LogTrace($"Enum type {fieldType.Name} (underlying {enumUnderlyingType.Name}), size: {enumSize} bytes");
                        }
                        else if (fieldType == typeof(string))
                        {
                            // 字符串特殊处理：包含对象头、字符数组和字符串内容
                            if (value != null)
                            {
                                string str = (string)value;
                                // 字符串对象头：24 字节（16字节对象头 + 8字节数组引用）
                                size += 24;
                                // 字符数组：每个字符占 2 字节（Unicode），加上数组头 16 字节
                                size += (long)str.Length * 2 + 16;
                                Logger.Instance.LogDebug($"String value '{str.Substring(0, Math.Min(str.Length, 20))}...', size: {24 + str.Length * 2 + 16} bytes");
                            }
                            else
                            {
                                // 空引用：指针大小（32位/64位系统）
                                size += IntPtr.Size;
                                Logger.Instance.LogTrace($"Null string reference, size: {IntPtr.Size} bytes");
                            }
                        }
                        else if (fieldType.IsArray)
                        {
                            // 数组处理：包含数组头和元素数据
                            Array array = value as Array;
                            if (array != null)
                            {
                                Type elementType = fieldType.GetElementType();
                                long elementSize = Marshal.SizeOf(elementType);
                                // 数组头：16 字节
                                size += 16;
                                // 元素总大小
                                size += array.Length * elementSize;
                                Logger.Instance.LogDebug($"Array of {elementType.Name} ({array.Length} elements), size: 16 + {array.Length * elementSize} = {16 + array.Length * elementSize} bytes");
                            }
                            else
                            {
                                // 空数组引用
                                size += IntPtr.Size;
                                Logger.Instance.LogTrace($"Null array reference, size: {IntPtr.Size} bytes");
                            }
                        }
                        else
                        {
                            // 其他引用类型：仅计算指针大小
                            size += IntPtr.Size;
                            Logger.Instance.LogTrace($"Reference type {fieldType.Name}, size: {IntPtr.Size} bytes");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 记录 Error 日志：字段读取异常（如非公共字段无访问权限）
                        Logger.Instance.LogError($"Error getting field {field.Name} value: {ex.Message} {ex}");
                    }
                }

                // 记录 Info 日志：返回计算结果
                Logger.Instance.LogInformation($"Calculated memory size for {type.FullName}: {size} bytes");
                return size;
            }
            catch (Exception ex)
            {
                // 记录 Critical 日志：内存计算过程中出现致命异常
                Logger.Instance.LogCritical($"Fatal error in memory calculation for {type.FullName}: {ex.Message} {ex}");
                throw;
            }
        }
    }
}
