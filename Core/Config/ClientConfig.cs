using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;

namespace Server.Core.Config
{
    public class ClientConfig
    {
        private long _bytesReceived;
        private long _bytesSent;
        private long _FileBytesReceived;
        private long _FileBytesSent;
        private long _ReceiveCount;
        private long _SendCount;
        private long _ReceiveFileCount;
        private long _SendFileCount;
        private int _Seq;
        private int _UniqueId;
        private bool _isValueSet = false;
        public long FileBytesReceived => _FileBytesReceived;
        public long FileBytesSent => _FileBytesSent;
        public long BytesReceived => _bytesReceived;
        public long BytesSent => _bytesSent;
        public long ReceiveCount => _ReceiveCount;
        public long SendCount => _SendCount;
        public long ReceiveFileCount => _ReceiveFileCount;
        public long SendFileCount => _SendFileCount;
        public int Seq => _Seq;
        public int UniqueId => _UniqueId;
        public uint Id { get; }
        public Socket Socket { get; }
        public SslStream SslStream { get; }
        public DateTime LastActivity { get; set; }
        public DateTime StartActivity { get; set; }
        public Stopwatch ConnectionWatch { get; } = Stopwatch.StartNew();
        public string FilePath { get; set; }
        public bool IsConnect { get; set; }
        public ClientConfig(uint id, Socket socket)
        {
            Id = id;
            Socket = socket;
            LastActivity = DateTime.Now;

            FilePath = "Client" + id.ToString();

            IsConnect = true;

            StartActivity = DateTime.Now;
        }
        public ClientConfig(uint id, SslStream sslStream)
        {
            Id = id;
            SslStream = sslStream;

            LastActivity = DateTime.Now;

            FilePath = "Client" + id.ToString();

            IsConnect = true;

            StartActivity = DateTime.Now;
        }

        public void AddFileReceivedBytes(long bytes)
        {
            Interlocked.And(ref _ReceiveFileCount, 1);
            Interlocked.Add(ref _FileBytesReceived, bytes);
        }

        public void AddFileSentBytes(long bytes)
        {
            Interlocked.And(ref _SendFileCount, 1);
            Interlocked.Add(ref _FileBytesSent, bytes);
        }

        public void AddReceivedBytes(long bytes)
        {
            Interlocked.And(ref _ReceiveCount, 1);
            Interlocked.Add(ref _bytesReceived, bytes);
        }

        public void AddSentBytes(long bytes)
        {
            Interlocked.Add(ref _SendCount, 1);
            Interlocked.Add(ref _bytesSent, bytes);
        }

        public void UpdateActivity()
        {
            LastActivity = DateTime.Now;
        }

        public void UpdateSeq()
        {
            Interlocked.Increment(ref _Seq);
        }

        public void SetValue(int value)
        {
            if (!_isValueSet)
            {
                _UniqueId = value;
                _isValueSet = true;
            }
        }
    }

}
