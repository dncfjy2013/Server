using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Shared_Memory.High
{
    /// <summary>
    /// 共享内存状态
    /// </summary>
    public enum SharedMemoryState
    {
        Created,    // 已创建
        Opened,     // 已打开
        Closed,     // 已关闭
        Disposed    // 已释放
    }

    /// <summary>
    /// 共享内存接口
    /// </summary>
    public interface ISharedMemory : IDisposable
    {
        string Name { get; }             // 共享内存名称
        long Size { get; }              // 内存大小
        SharedMemoryState State { get; } // 当前状态

        bool CreateOrOpen(long size);    // 创建或打开共享内存
        bool OpenExisting();             // 打开已存在的共享内存
        void Close();                    // 关闭共享内存

        bool Write(byte[] data, int offset = 0);  // 写入数据
        bool Read(out byte[] data, int offset = 0, int? length = null); // 读取数据

        bool TryAcquireLock(int timeoutMs = Timeout.Infinite); // 获取锁
        void ReleaseLock(); // 释放锁

        event EventHandler<DataAvailableEventArgs> DataAvailable; // 数据可用事件
    }

    /// <summary>
    /// 数据可用事件参数
    /// </summary>
    public class DataAvailableEventArgs : EventArgs
    {
        public int DataLength { get; }
        public DateTime Timestamp { get; }

        public DataAvailableEventArgs(int dataLength)
        {
            DataLength = dataLength;
            Timestamp = DateTime.Now;
        }
    }
}
