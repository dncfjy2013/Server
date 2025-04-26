using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    // 文件传输信息类
    public class FileTransferInfo
    {
        public string FileId { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public int TotalChunks { get; set; }
        public int ChunkSize { get; set; }
        public string FilePath { get; set; }
        public ConcurrentDictionary<int, byte[]> ReceivedChunks { get; set; }
    }
}
