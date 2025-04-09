using Microsoft.VisualBasic.FileIO;
using Server.Common;
using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;

[Serializable]
public class CommunicationData
{
    public string Message { get; set; }
    public InfoType InfoType { get; set; }
    public int SeqNum { get; set; }     // 序列号
    public int AckNum { get; set; }     // 确认号
    public string FileId { get; set; }
    public string FileName { get; set; }
    public int TotalChunks { get; set; }
    public string MD5Hash { get; set; }
    public List<FileChunk> FileChunks { get; set; }
    public List<int> ReceivedChunks { get; set; }
}
public class FileChunk
{
    public int Index { get; set; }
    public byte[] Data { get; set; }
}

public class FileTransferInfo
{
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public int TotalChunks { get; set; }
    public HashSet<int> ReceivedChunks { get; set; }
    public long TotalReceivedBytes { get; set; } // 新增总接收字节统计
    public DateTime StartTime { get; set; } = DateTime.Now; // 记录传输开始时间
}

public class FileTransferState
{
    public string FileId { get; set; }
    public string FileName { get; set; }
    public int TotalChunks { get; set; }
    public string MD5Hash { get; set; }
    public HashSet<int> SentChunks { get; set; }
    public Action<int> ProgressCallback { get; set; }
    public bool Cancelled { get; set; }
    public string FilePath { get; set; }
}

// 公共辅助类
public static class Constants
{
    public const int ChunkSize = 1024 * 1024; // 1MB分块
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}