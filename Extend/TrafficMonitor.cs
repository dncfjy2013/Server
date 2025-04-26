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
        private readonly ConcurrentDictionary<int, ClientConfig> _clients;
        private readonly object _lock = new();
        private readonly int _monitorInterval;
        private readonly Stopwatch _totalTrafficWatch = new();
        private readonly ConcurrentDictionary<int, (long Received, long Sent, long FileReceived, long FileSent,
            long ReceivedCount, long SentCount, long FileReceivedCount, long FileSentCount, Stopwatch ConnectionWatch)> _clientTrafficStats = new();
        private readonly ConcurrentDictionary<int, (long Received, long Sent, long FileReceived, long FileSent,
            long ReceivedCount, long SentCount, long FileReceivedCount, long FileSentCount, Stopwatch ConnectionWatch)> _clientHistoryTrafficStats = new();
        private readonly int _samplingRate = 1; // Sample every 10th execution
        private int _sampleCounter;
        private bool _enableTrafficMonitoring = false;
        private Logger logger = new Logger();

        public TrafficMonitor(ConcurrentDictionary<int, ClientConfig> clients, int monitorInterval)
        {
            _clients = clients;
            _monitorInterval = monitorInterval;
            _totalTrafficWatch.Start();
            foreach (var client in _clients.Values)
            {
                _clientTrafficStats[client.Id] = (client.BytesReceived, client.BytesSent, client.FileBytesReceived, client.FileBytesSent, client.ReceiveCount, client.SendCount, client.ReceiveFileCount, client.SendFileCount, client.ConnectionWatch);
            }

        }

        public void ModifyEnable(bool enable)
        {
            _enableTrafficMonitoring = enable;
        }

        public void Monitor()
        {
            if (!_enableTrafficMonitoring) return;

            _sampleCounter++;
            if (_sampleCounter % _samplingRate != 0) return; // Sampling mechanism

            lock (_lock)
            {
                // 同步新增客户端
                foreach (var client in _clients.Values)
                {
                    if (!_clientTrafficStats.ContainsKey(client.Id))
                    {
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

                // 清理已断开客户端
                var activeClientIds = _clients.Keys;
                var staleEntries = _clientTrafficStats.Keys.Where(k => !activeClientIds.Contains(k)).ToList();
                foreach (var id in staleEntries)
                {
                    var oldclient = _clientTrafficStats[id];
                    _clientTrafficStats.TryRemove(id, out _);
                    _clientHistoryTrafficStats.TryAdd(id, oldclient);
                }

                foreach (var client in _clients.Values)
                {
                    if (_clientTrafficStats.TryGetValue(client.Id, out var stats))
                    {
                        var (received, sent, fileRec, fileSent, receivedcount, sentcount, fileReccount, fileSentcount, connectionWatch) = stats;

                        var recDiff = client.BytesReceived - received;
                        var sentDiff = client.BytesSent - sent;
                        var fileRecDiff = client.FileBytesReceived - fileRec;
                        var fileSentDiff = client.FileBytesSent - fileSent;

                        var recCDiff = client.ReceiveCount - receivedcount;
                        var sentCDiff = client.SendCount - sentcount;
                        var fileRecCDiff = client.ReceiveFileCount - fileReccount;
                        var fileSentCDiff = client.SendFileCount - fileSentcount;

                        TimeSpan time = (client.ConnectionWatch.Elapsed - connectionWatch.Elapsed).Duration();

                        var message = $"[Client ID: {client.Id}] " +
                                      $"Normal: Recv {Function.FormatBytes(recDiff)} Send {Function.FormatBytes(sentDiff)} | " +
                                      $"File: Recv {Function.FormatBytes(fileRecDiff)} Send {Function.FormatBytes(fileSentDiff)} | " +
                                      $"Total: Recv {Function.FormatBytes(recDiff + fileRecDiff)} Send {Function.FormatBytes(sentDiff + fileSentDiff)} | " +
                                      $"Count Normal: Recv {recCDiff} Send {sentCDiff} | " +
                                      $"Coount File: Recv {fileRecCDiff} Send {fileSentCDiff} | " +
                                      $"Count Total: Recv {recCDiff + fileRecCDiff} Send {sentCDiff + fileSentCDiff} | " +
                                      $"History Normal: Recv {Function.FormatBytes(client.BytesReceived)} Send {Function.FormatBytes(client.BytesSent)} | " +
                                      $"History File: Recv {Function.FormatBytes(client.FileBytesReceived)} Send {Function.FormatBytes(client.FileBytesSent)} | " +
                                      $"History Total: Recv {client.BytesReceived + client.FileBytesReceived} Send {Function.FormatBytes(client.BytesSent + client.FileBytesSent)} | " +
                                      $"History Count Normal: Recv {client.ReceiveCount} Send {client.SendCount} | " +
                                      $"History Count File: Recv {client.ReceiveFileCount} Send {client.SendFileCount} | " +
                                      $"History Count Total: Recv {client.ReceiveCount + client.ReceiveFileCount} Send {client.SendCount + client.SendFileCount} | " +
                                      $"Connect Time: {client.ConnectionWatch.Elapsed:mm\\:ss}";

                        // Update stats for next interval
                        _clientTrafficStats[client.Id] = (client.BytesReceived, client.BytesSent, client.FileBytesReceived, client.FileBytesSent, client.ReceiveCount, client.SendCount, client.ReceiveFileCount, client.SendFileCount, connectionWatch);
                        logger.LogDebug(message);
                    }
                }
                long recv = _clients.Values.Sum(c => c.BytesReceived);
                long send = _clients.Values.Sum(c => c.BytesSent);
                long filerecv = _clients.Values.Sum(c => c.FileBytesReceived);
                long filesend = _clients.Values.Sum(c => c.FileBytesSent);

                long recvc = _clients.Values.Sum(c => c.ReceiveCount);
                long sendc = _clients.Values.Sum(c => c.SendCount);
                long filerecvc = _clients.Values.Sum(c => c.ReceiveFileCount);
                long filesendc = _clients.Values.Sum(c => c.SendFileCount);

                if (recv != 0 || send != 0 || filerecv != 0 || filesend != 0)
                {
                    // Optionally log total traffic if needed
                    var totalMessage = $"Active Total Traffic: " +
                                       $"Nornal Recv {Function.FormatBytes(recv)}, Sent {Function.FormatBytes(send)}" +
                                       $"File Recv {Function.FormatBytes(filerecv)}, Sent {Function.FormatBytes(filesend)}" +
                                       $"Total: Recv {Function.FormatBytes(recv + filerecv)} Send {Function.FormatBytes(send + filesend)}"+
                                       $"Count Nornal Recv {recvc}, Sent {sendc}" +
                                       $"Count File Recv {filerecvc}, Sent {filesendc}" +
                                       $"Count Total: Recv {recvc + filerecvc} Send {sendc + filesendc}";
                    logger.LogDebug(totalMessage);
                }

                DateTime currentTime = DateTime.Now;
                DateTime today = DateTime.Today;
                bool isPast24 = currentTime.Date > today;

                if(isPast24)
                {
                    _clientHistoryTrafficStats.Clear();
                }
            }
        }

    }
    public static class MemoryCalculator
    {
        public static long CalculateObjectSize<T>(T obj) where T : class
        {
            long size = 0;

            // 获取类型信息
            Type type = typeof(T);

            // 对象头部开销（16字节：同步块索引 + 类型对象指针）
            size += 16;

            // 遍历所有字段
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                object value = field.GetValue(obj);
                Type fieldType = field.FieldType;

                // 计算基础类型大小
                if (fieldType.IsPrimitive)
                {
                    size += Marshal.SizeOf(fieldType);
                }
                // 计算枚举类型大小（按基础类型计算）
                else if (fieldType.IsEnum)
                {
                    Type underlyingType = Enum.GetUnderlyingType(fieldType);
                    size += Marshal.SizeOf(underlyingType);
                }
                // 计算字符串大小
                else if (fieldType == typeof(string))
                {
                    if (value != null)
                    {
                        // 字符串对象自身开销（24字节：对象头+字符串长度+字符数组引用）
                        size += 24;
                        // 字符数组开销（长度*2 + 数组对象头）
                        string str = (string)value;
                        size += (long)str.Length * 2 + 16;
                    }
                    else
                    {
                        size += IntPtr.Size; // 引用指针大小
                    }
                }
                // 计算数组大小
                else if (fieldType.IsArray)
                {
                    Array array = value as Array;
                    if (array != null)
                    {
                        // 数组对象头（16字节）
                        size += 16;
                        // 数组元素大小
                        size += array.Length * Marshal.SizeOf(fieldType.GetElementType());
                    }
                    else
                    {
                        size += IntPtr.Size; // 引用指针大小
                    }
                }
                // 其他引用类型
                else
                {
                    size += IntPtr.Size; // 引用指针大小
                }
            }

            return size;
        }
    }
}
