using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class ClientConfig
    {
        public int Id { get; }
        public Socket Socket { get; }
        public SslStream SslStream { get; }
        public DateTime LastHeartbeat { get; set; }
        public Stopwatch ConnectionWatch { get; } = Stopwatch.StartNew();
        public DateTime LastActivity { get; set; }
        public int ExpectedAck { get; set; }

        public string FilePath { get; set; }
        public bool IsConnect {  get; set; }
        public ClientConfig(int id, Socket socket)
        {
            Id = id;
            Socket = socket;
            LastHeartbeat = DateTime.Now;
            LastActivity = DateTime.Now;

            FilePath = "Client" + id.ToString();

            IsConnect = true;
        }
        public ClientConfig(int id, SslStream sslStream)
        {
            Id = id;
            SslStream = sslStream;

            LastHeartbeat = DateTime.Now;
            LastActivity = DateTime.Now;

            FilePath = "Client" + id.ToString();

            IsConnect = true;
        }

        private int _bytesReceived;
        private int _bytesSent;
        // 新增大文件流量统计
        private long _FileBytesReceived;
        private long _FileBytesSent;

        public long FileBytesReceived
        {
            get => _FileBytesReceived;
            set => Interlocked.Exchange(ref _FileBytesReceived, value);
        }
        public long FileBytesSent
        {
            get => _FileBytesSent;
            set => Interlocked.Exchange(ref _FileBytesSent, value);
        }

        public void AddFileReceivedBytes(long bytes) => Interlocked.Add(ref _FileBytesReceived, bytes);
        public void AddFileSentBytes(long bytes) => Interlocked.Add(ref _FileBytesSent, bytes);
        public int BytesReceived
        {
            get => _bytesReceived;
            set => Interlocked.Exchange(ref _bytesReceived, value);
        }

        public int BytesSent
        {
            get => _bytesSent;
            set => Interlocked.Exchange(ref _bytesSent, value);
        }

        public void AddReceivedBytes(int bytes) =>
            Interlocked.Add(ref _bytesReceived, bytes);

        public void AddSentBytes(int bytes) =>
            Interlocked.Add(ref _bytesSent, bytes);

        public void UpdateHeartbeat()
        {
            LastHeartbeat = DateTime.Now;
        }
    }

}
