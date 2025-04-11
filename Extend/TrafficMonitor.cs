using Microsoft.VisualBasic;
using Server.Client;
using Server.Common;
using Server.Common.Log;
using Server.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private readonly ConcurrentDictionary<int, (long Received, long Sent, long FileReceived, long FileSent, Stopwatch ConnectionWatch)> _clientTrafficStats = new();
        private readonly ConcurrentDictionary<int, (long Received, long Sent, long FileReceived, long FileSent, Stopwatch ConnectionWatch)> _clientHistoryTrafficStats = new();
        private readonly int _samplingRate = 1; // Sample every 10th execution
        private int _sampleCounter;
        private bool _enableTrafficMonitoring = false;
        private Logger logger = Logger.GetInstance();

        public TrafficMonitor(ConcurrentDictionary<int, ClientConfig> clients, int monitorInterval)
        {
            _clients = clients;
            _monitorInterval = monitorInterval;
            _totalTrafficWatch.Start();
            foreach (var client in _clients.Values)
            {
                _clientTrafficStats[client.Id] = (client.BytesReceived, client.BytesSent, client.FileBytesReceived, client.FileBytesSent, client.ConnectionWatch);
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
                        var (received, sent, fileRec, fileSent, connectionWatch) = stats;

                        var recDiff = client.BytesReceived - received;
                        var sentDiff = client.BytesSent - sent;
                        var fileRecDiff = client.FileBytesReceived - fileRec;
                        var fileSentDiff = client.FileBytesSent - fileSent;
                        TimeSpan time = (client.ConnectionWatch.Elapsed - connectionWatch.Elapsed).Duration();

                        var message = $"[Client ID: {client.Id}] " +
                                      $"Normal: Recv {Function.FormatBytes(recDiff)} Send {Function.FormatBytes(sentDiff)} | " +
                                      $"File: Recv {Function.FormatBytes(fileRecDiff)} Send {Function.FormatBytes(fileSentDiff)} | " +
                                      $"Total: Recv {Function.FormatBytes(recDiff + fileRecDiff)} Send {Function.FormatBytes(sentDiff + fileSentDiff)} | " +
                                      $"History Normal: Recv {Function.FormatBytes(client.BytesReceived)} Send {Function.FormatBytes(client.BytesSent)} | " +
                                      $"History File: Recv {Function.FormatBytes(client.FileBytesReceived)} Send {Function.FormatBytes(client.FileBytesSent)} | " +
                                      $"History Total: Recv {Function.FormatBytes(recDiff + fileRecDiff)} Send {Function.FormatBytes(sentDiff + fileSentDiff)} | " +
                                      $"Connect Time: {client.ConnectionWatch.Elapsed:mm\\:ss}";

                        // Update stats for next interval
                        _clientTrafficStats[client.Id] = (client.BytesReceived, client.BytesSent, client.FileBytesReceived, client.FileBytesSent, connectionWatch);
                        logger.LogTemp(LogLevel.Info, message);
                    }
                }
                long recv = _clients.Values.Sum(c => c.BytesReceived);
                long send = _clients.Values.Sum(c => c.BytesSent);
                long filerecv = _clients.Values.Sum(c => c.FileBytesReceived);
                long filesend = _clients.Values.Sum(c => c.FileBytesSent);

                if (recv != 0 || send != 0 || filerecv != 0 || filesend != 0)
                {
                    // Optionally log total traffic if needed
                    var totalMessage = $"Active Total Traffic: " +
                                       $"Nornal Recv {Function.FormatBytes(recv)}, Sent {Function.FormatBytes(send)}" +
                                       $"File Recv {Function.FormatBytes(filerecv)}, Sent {Function.FormatBytes(filesend)}" +
                                       $"Total: Recv {Function.FormatBytes(recv + filerecv)} Send {Function.FormatBytes(send + filesend)}";
                    logger.LogTemp(LogLevel.Info, totalMessage);
                }

                long oldrecv = _clients.Values.Sum(c => c.BytesReceived);
                long oldsend = _clients.Values.Sum(c => c.BytesSent);
                long oldfilerecv = _clients.Values.Sum(c => c.FileBytesReceived);
                long oldfilesend = _clients.Values.Sum(c => c.FileBytesSent);

                if (oldrecv != 0 || oldsend != 0 || oldfilerecv != 0 || oldfilesend != 0)
                {
                    // Optionally log total traffic if needed
                    var totalMessage = $"Total Traffic: " +
                                       $"Nornal Recv {Function.FormatBytes(oldrecv + recv)}, Sent {Function.FormatBytes(oldsend + send)}" +
                                       $"File Recv {Function.FormatBytes(oldfilerecv + oldfilerecv)}, Sent {Function.FormatBytes(oldfilesend + filesend)}" +
                                       $"Total: Recv {Function.FormatBytes(oldrecv + oldfilerecv + recv + filerecv)} Send {Function.FormatBytes(oldsend + oldfilesend + send + filesend)}";
                    logger.Log(LogLevel.Info, totalMessage);
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
}
