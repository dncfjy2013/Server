using Server.Proxy.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace Server.Proxy.Config
{
    public class TargetServer
    {
        private int _currentConnections;

        public string Ip { get; }
        public int Port { get; }
        public int TargetPort { get; }
        public ConnectType BackendProtocol { get; set; } = ConnectType.Tcp;
        public X509Certificate2 ClientCertificate { get; set; }
        public int CurrentConnections { get; set; }

        // HTTP 专属配置
        public string HttpPath { get; set; } = "/";
        public bool StripPath { get; set; } = true;
        public Dictionary<string, string> RequestHeaders { get; } = new();
        public TimeoutValue Timeout { get; set; } = new TimeoutValue(TimeSpan.FromSeconds(30));

        // 负载均衡相关属性
        public int Weight { get; set; } = 5;
        public double AverageResponseTimeMs { get; set; }
        public string Zone { get; set; }
        public bool IsHealthy { get; set; } = true;

        // 统计指标
        private int _Http2xxCount;
        private int _Http3xxCount;
        private int _Http4xxCount;
        private int _Http5xxCount;
        public int RequestCount { get; private set; }
        public int Http2xxCount => _Http2xxCount;
        public int Http3xxCount => _Http3xxCount;
        public int Http4xxCount => _Http4xxCount;
        public int Http5xxCount => _Http5xxCount;

        public TargetServer(string ip, int port, int targetPort, string zone, int weight = 5)
        {
            Ip = ip;
            Port = port;
            TargetPort = targetPort;
            Zone = zone;
            Weight = weight;
        }

        public TargetServer(string ip, int port, int targetPort, string zone, string httpPath, int weight = 5)
            : this(ip, port, targetPort, zone, weight)
        {
            HttpPath = httpPath;
        }

        public void Increment() => Interlocked.Increment(ref _currentConnections);
        public void Decrement() => Interlocked.Decrement(ref _currentConnections);
        public void Increment(int delta) => Interlocked.Add(ref _currentConnections, delta);

        public void UpdateResponseTime(long elapsedMs)
        {
            // 指数加权平均计算响应时间
            if (RequestCount == 0)
            {
                AverageResponseTimeMs = elapsedMs;
            }
            else
            {
                AverageResponseTimeMs = (AverageResponseTimeMs * 0.8) + (elapsedMs * 0.2);
            }
            RequestCount++;
        }

        public void UpdateHttpStatus(int statusCode)
        {
            switch (statusCode / 100)
            {
                case 2: Interlocked.Increment(ref _Http2xxCount); break;
                case 3: Interlocked.Increment(ref _Http3xxCount); break;
                case 4: Interlocked.Increment(ref _Http4xxCount); break;
                case 5: Interlocked.Increment(ref _Http5xxCount); break;
            }
        }
    }

    public class TimeoutValue
    {
        public TimeSpan TimeSpan { get; }
        public TimeoutValue(TimeSpan timeSpan) => TimeSpan = timeSpan;
        public TimeoutValue(string timeSpanString) => TimeSpan = TimeSpan.Parse(timeSpanString);
    }
}