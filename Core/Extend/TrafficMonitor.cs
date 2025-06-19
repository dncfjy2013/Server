using Server.Core.Config;
using Server.Logger;
using Server.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Server.Core.Extend
{
    public class TrafficMonitor
    {
        // 用于存储客户端配置的并发字典，键为客户端的唯一标识（整数类型），值为客户端配置对象
        // 并发字典支持多线程安全访问，适合在高并发场景下存储和管理客户端信息
        private readonly ConcurrentDictionary<uint, ClientConfig> _clients;

        // 用于线程同步的锁对象，在需要对共享资源进行线程安全操作时使用
        // 通过使用锁，可以确保同一时间只有一个线程能够访问被保护的代码块
        private readonly object _lock = new();

        // 监控间隔时间（单位可能为毫秒等，具体取决于业务逻辑）
        // 该值决定了监控任务执行的时间间隔，例如每隔一定时间检查一次客户端的状态等
        private int _monitorInterval;

        // 用于统计总流量的秒表，可记录从开始到结束的时间间隔
        // 可用于计算一段时间内的总流量，辅助进行流量监控和性能分析
        private readonly Timer _totalTrafficWatch;

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
        private readonly ConcurrentDictionary<uint, (long Received, long Sent, long FileReceived, long FileSent,
            long ReceivedCount, long SentCount, long FileReceivedCount, long FileSentCount, Stopwatch ConnectionWatch)> _clientTrafficStats = new();

        // 用于存储每个客户端的历史流量统计信息的并发字典
        // 结构与 _clientTrafficStats 类似，但存储的是历史数据，可用于后续的数据分析和报告
        private readonly ConcurrentDictionary<uint, (long Received, long Sent, long FileReceived, long FileSent,
            long ReceivedCount, long SentCount, long FileReceivedCount, long FileSentCount, Stopwatch ConnectionWatch)> _clientHistoryTrafficStats = new();

        // 采样率，用于控制数据采样的频率
        // 例如，值为 1 表示每执行一次就进行采样，值为 10 表示每 10 次执行进行一次采样
        private readonly int _samplingRate = 1;

        // 采样计数器，用于记录执行次数，配合采样率进行数据采样
        // 每执行一次相关操作，该计数器加 1，当达到采样率的倍数时进行数据采样
        private int _sampleCounter;

        // 流量监控功能的启用标志
        private bool _enableTrafficMonitoring = false;

        // 日志记录器实例
        private ILogger _logger;

        // 新增资源释放标志
        private bool _disposed = false;

        /// <summary>
        /// 流量监控器的构造函数，用于初始化监控器并启动流量监控
        /// </summary>
        /// <param name="clients">存储客户端配置的并发字典</param>
        /// <param name="monitorInterval">监控的时间间隔</param>
        public TrafficMonitor(ConcurrentDictionary<uint, ClientConfig> clients, int monitorInterval, ILogger logger)
        {
            _logger = logger;

            // 记录 Trace 日志，表明开始执行构造函数
            _logger.LogTrace("Starting the constructor of TrafficMonitor.");

            try
            {
                // 将传入的客户端配置字典赋值给类的私有字段
                _clients = clients;
                // 记录 Debug 日志，显示客户端配置字典已成功赋值
                _logger.LogDebug("Clients configuration dictionary has been assigned.");

                // 将传入的监控时间间隔赋值给类的私有字段
                _monitorInterval = monitorInterval;
                // 记录 Debug 日志，显示监控时间间隔已成功赋值
                _logger.LogDebug($"Monitor interval has been set to {monitorInterval}.");

                // 启动总流量统计的秒表
                _totalTrafficWatch = new Timer(_ => Monitor(), null, Timeout.Infinite, Timeout.Infinite); ;
                // 记录 Info 日志，表明总流量统计秒表已启动
                _logger.LogDebug("Total traffic stopwatch has been started.");

                // 遍历所有客户端配置
                foreach (var client in _clients.Values)
                {
                    // 将客户端的初始流量统计信息存储到客户端实时流量统计字典中
                    _clientTrafficStats[client.Id] = (client.BytesReceived, client.BytesSent, client.FileBytesReceived, client.FileBytesSent, client.ReceiveCount, client.SendCount, client.ReceiveFileCount, client.SendFileCount, client.ConnectionWatch);
                    // 记录 Debug 日志，显示已为特定客户端初始化流量统计信息
                    _logger.LogDebug($"Initialized traffic statistics for client with ID {client.Id}.");
                }

                // 记录 Info 日志，表明所有客户端的流量统计信息已初始化完成
                _logger.LogInformation("Traffic statistics for all clients have been initialized.");
            }
            catch (Exception ex)
            {
                // 若在构造函数执行过程中出现异常，记录 Error 日志，显示异常信息
                _logger.LogError($"An error occurred during the initialization of TrafficMonitor: {ex.Message} {ex}");
            }

            // 记录 Trace 日志，表明构造函数执行结束
            _logger.LogTrace("Constructor of TrafficMonitor has been completed.");
        }

        /// <summary>
        /// 修改流量监控功能的启用状态
        /// </summary>
        /// <param name="enable">一个布尔值，true 表示启用流量监控，false 表示禁用流量监控</param>
        public void ModifyEnable(bool enable)
        {
            _logger.LogTrace($"Entering ModifyEnable method with enable value: {enable}");

            try
            {
                _enableTrafficMonitoring = enable;

                if (_enableTrafficMonitoring)
                {
                    _logger.LogDebug($"Starting the traffic monitor timer with an immediate start and interval of {_monitorInterval} ms.");
                    if(!_totalTrafficWatch.Change(0, _monitorInterval))
                    {
                        _logger.LogError($"Error Starting the traffic monitor timer with an immediate start and interval of {_monitorInterval} ms.");
                    }
                    else
                        _logger.LogDebug("Traffic monitor timer has been successfully started.");
                }
                else
                {
                    _logger.LogDebug($"Sttoping the traffic monitor timer");
                    if (!_totalTrafficWatch.Change(Timeout.Infinite, Timeout.Infinite))
                    {
                        _logger.LogError("Traffic monitor timer stoped error.");
                    }
                    else
                        _logger.LogDebug("Traffic monitor timer has been successfully stoped.");
                }

                _logger.LogInformation($"Traffic monitoring enable status has been modified to {(_enableTrafficMonitoring ? "enabled" : "disabled")}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while modifying the traffic monitoring enable status: {ex.Message} {ex}");
            }

            _logger.LogTrace("Exiting ModifyEnable method");
        }

        public double GetMonitorInterval()
        {
            _logger.LogTrace($"Return MonitorInterval with value: {_monitorInterval}ms");
            return _monitorInterval;
        }

        public bool SetMonitorInterval(int value)
        {
            _logger.LogTrace($"Entering SetMonitorInterval with value: {value}ms");

            try
            {
                if (value <= 0)
                {
                    _logger.LogWarning($"Invalid monitor interval value: {value}ms. Must be greater than 0");
                    return false;
                }

                _logger.LogDebug($"Attempting to set traffic monitor interval to {value}ms");
                _monitorInterval = value;

                _logger.LogDebug($"Starting timer change operation with interval: {value}ms");
                if (!_totalTrafficWatch.Change(0, value))
                {
                    _logger.LogError($"Failed to update traffic monitor interval to {value}ms. Timer state: {_disposed}");
                    return false;
                }

                _logger.LogInformation($"Traffic monitor interval successfully updated to {value}ms");
                return true;
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogError("Timer has already been disposed. Cannot modify interval");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unexpected error setting monitor interval: {ex.Message}");
                return false;
            }
            finally
            {
                _logger.LogTrace($"Exiting SetMonitorInterval. Current interval: {_monitorInterval}ms");
            }
        }
        /// <summary>
        /// 流量监控主方法（按采样率统计客户端流量数据）
        /// </summary>
        public void Monitor()
        {
            if (!_enableTrafficMonitoring)
            {
                _logger.LogTrace("Traffic monitoring is disabled, skipping monitor");
                return;
            }

            // 采样计数器递增
            _sampleCounter++;
            // 采样机制：仅当计数器是采样率的整数倍时执行统计
            if (_sampleCounter % _samplingRate != 0)
            {
                _logger.LogTrace($"Sampling counter {_sampleCounter} not reached threshold {_samplingRate}, skipping");
                return;
            }

            _logger.LogDebug($"Entering monitor with sample counter {_sampleCounter}");

            // 加锁确保线程安全（操作共享的客户端统计数据）
            lock (_lock)
            {
                try
                {
                    #region 同步新增客户端流量统计
                    _logger.LogTrace("Syncing new client entries");
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
                            _logger.LogInformation($"New client {client.Id} traffic stats initialized");
                        }
                    }
                    #endregion

                    #region 清理已断开客户端的过时数据
                    _logger.LogTrace("Cleaning up stale client entries");
                    var activeClientIds = _clients.Keys;
                    var staleEntries = _clientTrafficStats.Keys.Where(k => !activeClientIds.Contains(k)).ToList();

                    foreach (var id in staleEntries)
                    {
                        if (_clientTrafficStats.TryRemove(id, out var oldStats))
                        {
                            // 移动旧数据到历史统计
                            _clientHistoryTrafficStats.TryAdd(id, oldStats);
                            _logger.LogWarning($"Client {id} disconnected, moved stats to history");
                        }
                    }
                    #endregion

                    #region 计算单个客户端流量差异
                    _logger.LogTrace("Calculating per-client traffic deltas");
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
                            _logger.LogDebug(message);

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
                    _logger.LogTrace("Calculating global traffic statistics");
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
                        _logger.LogInformation(totalMessage);
                    }
                    #endregion

                    #region 每日历史数据清理
                    _logger.LogTrace("Checking for daily history cleanup");
                    var currentTime = DateTime.Now;
                    if (currentTime.Date > DateTime.Today) // 跨天后执行清理
                    {
                        _clientHistoryTrafficStats.Clear();
                        _logger.LogInformation("Daily traffic history cleared");
                    }
                    #endregion
                }
                catch (Exception ex)
                {
                    // 记录致命错误（如锁竞争、数据解析异常）
                    _logger.LogError($"Critical error in traffic monitor: {ex.Message} {ex}");
                }
            }
        }
        /// <summary>
        /// 释放非托管资源并执行清理操作
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 实际资源释放逻辑（支持继承）
        /// </summary>
        /// <param name="disposing">true表示显式调用Dispose，false表示通过析构函数释放</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 停止并释放定时器资源
                try
                {
                    // 停止定时器（即使未启动也安全）
                    _totalTrafficWatch.Change(Timeout.Infinite, Timeout.Infinite);
                    // 显式释放定时器资源
                    _totalTrafficWatch.Dispose();
                    _logger.LogInformation("Traffic monitor timer has been disposed");
                }
                catch (ObjectDisposedException)
                {
                    // 忽略已释放的异常
                    _logger.LogTrace("Timer already disposed");
                }
            }

            // 清理其他托管资源（本例中主要为定时器）
            _disposed = true;
        }

        // 析构函数（作为安全网，但优先使用Dispose）
        ~TrafficMonitor()
        {
            Dispose(false);
        }
    }

}
