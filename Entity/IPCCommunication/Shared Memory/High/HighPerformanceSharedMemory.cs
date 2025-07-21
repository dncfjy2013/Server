using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Entity.IPCCommunication.Shared_Memory.High
{
    /// <summary>
    /// 高性能共享内存实现（支持双缓冲和高并发）
    /// </summary>
    public sealed class HighPerformanceSharedMemory : ISharedMemory
    {
        // 内存布局常量
        private const int HEADER_SIZE = 1024;                // 头部大小（字节）
        private const int BUFFER_COUNT = 2;                  // 双缓冲
        private const int LOCK_TIMEOUT = 5000;               // 默认锁超时（毫秒）
        private const int CACHE_LINE_SIZE = 64;              // 缓存行大小（字节）

        // 共享内存头部结构（按缓存行对齐）
        [StructLayout(LayoutKind.Explicit, Size = HEADER_SIZE)]
        private struct SharedMemoryHeader
        {
            // 当前活动缓冲区索引（使用Interlocked原子操作）
            [FieldOffset(0)]
            public int ActiveBufferIndex;

            // 每个缓冲区的状态（使用Interlocked原子操作）
            [FieldOffset(CACHE_LINE_SIZE)]
            public int Buffer1Status;

            [FieldOffset(CACHE_LINE_SIZE * 2)]
            public int Buffer2Status;

            // 每个缓冲区的数据长度
            [FieldOffset(CACHE_LINE_SIZE * 3)]
            public int Buffer1DataLength;

            [FieldOffset(CACHE_LINE_SIZE * 4)]
            public int Buffer2DataLength;

            // 锁状态（使用Interlocked原子操作）
            [FieldOffset(CACHE_LINE_SIZE * 5)]
            public int Lock;

            // 引用计数（使用Interlocked原子操作）
            [FieldOffset(CACHE_LINE_SIZE * 6)]
            public int ReferenceCount;

            // 最后更新时间戳
            [FieldOffset(CACHE_LINE_SIZE * 7)]
            public long LastUpdateTimestamp;
        }

        // 缓冲区状态
        private const int BUFFER_EMPTY = 0;      // 缓冲区为空，可写入
        private const int BUFFER_WRITING = 1;    // 正在写入缓冲区
        private const int BUFFER_READY = 2;      // 缓冲区数据就绪，可读取
        private const int BUFFER_READING = 3;    // 正在读取缓冲区

        // 锁状态
        private const int LOCK_FREE = 0;         // 锁空闲
        private const int LOCK_TAKEN = 1;        // 锁已被占用

        // 共享内存的唯一标识名称，用于跨进程识别同一共享内存区域
        private readonly string _name;

        // 内存映射文件对象，是跨进程共享内存的核心载体 底层通过Windows内核对象实现不同进程对同一块物理内存的访问
        private MemoryMappedFile _memoryMappedFile;

        // 共享内存头部区域的访问器，用于读写头部元数据（如缓冲区状态、锁信息等） 头部区域存储控制信息，不存放实际业务数据
        private MemoryMappedViewAccessor _headerAccessor;

        // 数据缓冲区的访问器数组，长度为BUFFER_COUNT（双缓冲即长度为2）每个访问器对应一个独立的缓冲区，用于读写实际业务数据
        private MemoryMappedViewAccessor[] _bufferAccessors = new MemoryMappedViewAccessor[BUFFER_COUNT];

        // 当前共享内存的状态（已创建/已打开/已关闭/已释放） 用于控制状态流转，防止非法操作（如对已关闭的内存执行读写）
        private SharedMemoryState _state;

        // 单个数据缓冲区的大小（字节），双缓冲总数据区大小为_bufferSize * 2 需在初始化时指定，应根据业务数据大小合理设置
        private long _bufferSize;

        // 轻量级手动重置事件，用于通知消费者"新数据已写入" 替代传统EventWaitHandle，性能更优，适合高频通知场景
        private readonly ManualResetEventSlim _dataAvailableEvent = new ManualResetEventSlim(false);

        // 用于同步Dispose操作的锁对象，防止多线程同时释放资源导致异常 确保Dispose/Close操作的线程安全性
        private readonly object _disposeLock = new object();

        // 标记对象是否已释放，防止重复释放资源
        private bool _isDisposed;

        // 数据可用事件，当新数据写入并切换缓冲区后触发 消费者可通过订阅此事件获取数据更新通知
        public event EventHandler<DataAvailableEventArgs> DataAvailable;

        // 共享内存名称的只读属性，对外暴露唯一标识
        public string Name => _name;

        // 共享内存总大小（字节），计算公式：头部大小 + 2个缓冲区大小 用于对外展示内存占用情况
        public long Size => _bufferSize * BUFFER_COUNT + HEADER_SIZE;

        // 当前状态的只读属性，对外暴露共享内存的生命周期状态
        public SharedMemoryState State => _state;

        public HighPerformanceSharedMemory(string name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _state = SharedMemoryState.Closed;
        }

        public bool CreateOrOpen(long size)
        {
            if (size <= 0)
                throw new ArgumentException("共享内存大小必须大于0", nameof(size));

            lock (_disposeLock)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(HighPerformanceSharedMemory));

                if (_state != SharedMemoryState.Closed)
                    return false;

                try
                {
                    _bufferSize = size;

                    // 创建或打开共享内存
                    _memoryMappedFile = MemoryMappedFile.CreateOrOpen(
                        _name,
                        HEADER_SIZE + _bufferSize * BUFFER_COUNT,
                        MemoryMappedFileAccess.ReadWrite);

                    // 创建头部访问器
                    _headerAccessor = _memoryMappedFile.CreateViewAccessor(0, HEADER_SIZE, MemoryMappedFileAccess.ReadWrite);

                    // 初始化头部（如果是新创建的）
                    if (Interlocked.Increment(ref GetHeaderReferenceCount()) == 1)
                    {
                        InitializeHeader();
                    }

                    // 创建缓冲区访问器
                    for (int i = 0; i < BUFFER_COUNT; i++)
                    {
                        long offset = HEADER_SIZE + i * _bufferSize;
                        _bufferAccessors[i] = _memoryMappedFile.CreateViewAccessor(offset, _bufferSize, MemoryMappedFileAccess.ReadWrite);
                    }

                    _state = SharedMemoryState.Created;

                    // 启动数据通知监听线程
                    StartDataNotificationThread();

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"创建或打开共享内存失败: {ex.Message}");
                    CloseInternal();
                    return false;
                }
            }
        }

        public bool OpenExisting()
        {
            lock (_disposeLock)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(HighPerformanceSharedMemory));

                if (_state != SharedMemoryState.Closed)
                    return false;

                try
                {
                    // 打开已存在的共享内存
                    _memoryMappedFile = MemoryMappedFile.OpenExisting(_name, MemoryMappedFileRights.ReadWrite);

                    // 获取头部信息
                    _headerAccessor = _memoryMappedFile.CreateViewAccessor(0, HEADER_SIZE, MemoryMappedFileAccess.ReadWrite);

                    // 增加引用计数
                    Interlocked.Increment(ref GetHeaderReferenceCount());

                    // 获取缓冲区大小
                    _bufferSize = (Size - HEADER_SIZE) / BUFFER_COUNT;

                    // 创建缓冲区访问器
                    for (int i = 0; i < BUFFER_COUNT; i++)
                    {
                        long offset = HEADER_SIZE + i * _bufferSize;
                        _bufferAccessors[i] = _memoryMappedFile.CreateViewAccessor(offset, _bufferSize, MemoryMappedFileAccess.ReadWrite);
                    }

                    _state = SharedMemoryState.Opened;

                    // 启动数据通知监听线程
                    StartDataNotificationThread();

                    return true;
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine($"共享内存不存在: {_name}");
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"打开共享内存失败: {ex.Message}");
                    CloseInternal();
                    return false;
                }
            }
        }

        public void Close()
        {
            lock (_disposeLock)
            {
                if (_isDisposed || _state == SharedMemoryState.Closed)
                    return;

                CloseInternal();
                _state = SharedMemoryState.Closed;
            }
        }

        public bool Write(byte[] data, int offset = 0)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (_state != SharedMemoryState.Created && _state != SharedMemoryState.Opened)
                throw new InvalidOperationException("共享内存未打开");

            if (data.Length > _bufferSize)
                throw new ArgumentException($"数据长度({data.Length})超过缓冲区大小({_bufferSize})", nameof(data));

            // 获取当前非活动缓冲区（用于写入）
            int activeBufferIndex = GetActiveBufferIndex();
            int writeBufferIndex = (activeBufferIndex + 1) % BUFFER_COUNT;

            // 尝试获取写入缓冲区的锁
            if (!TryAcquireBufferLock(writeBufferIndex, LOCK_TIMEOUT))
                return false;

            try
            {
                // 更新缓冲区状态为写入中
                SetBufferStatus(writeBufferIndex, BUFFER_WRITING);

                // 写入数据
                var accessor = _bufferAccessors[writeBufferIndex];
                accessor.WriteArray(0, data, 0, data.Length);

                // 更新数据长度
                SetBufferDataLength(writeBufferIndex, data.Length);

                // 更新时间戳
                SetLastUpdateTimestamp(DateTime.UtcNow.Ticks);

                // 交换缓冲区（原子操作）
                Interlocked.Exchange(ref GetHeaderActiveBufferIndex(), writeBufferIndex);

                // 更新缓冲区状态为就绪
                SetBufferStatus(writeBufferIndex, BUFFER_READY);

                // 触发数据可用事件
                _dataAvailableEvent.Set();
                DataAvailable?.Invoke(this, new DataAvailableEventArgs(data.Length));

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入共享内存失败: {ex.Message}");
                return false;
            }
            finally
            {
                // 释放缓冲区锁
                ReleaseBufferLock(writeBufferIndex);
            }
        }

        public bool Read(out byte[] data, int offset = 0, int? length = null)
        {
            data = null;

            if (_state != SharedMemoryState.Created && _state != SharedMemoryState.Opened)
                throw new InvalidOperationException("共享内存未打开");

            // 获取当前活动缓冲区（用于读取）
            int activeBufferIndex = GetActiveBufferIndex();

            // 检查缓冲区状态
            int status = GetBufferStatus(activeBufferIndex);
            if (status != BUFFER_READY)
                return false;

            // 获取数据长度
            int dataLength = GetBufferDataLength(activeBufferIndex);
            if (dataLength <= 0)
                return false;

            // 计算实际读取长度
            int readLength = length ?? dataLength;
            if (readLength > dataLength)
                readLength = dataLength;

            // 尝试获取读取缓冲区的锁
            if (!TryAcquireBufferLock(activeBufferIndex, LOCK_TIMEOUT))
                return false;

            try
            {
                // 更新缓冲区状态为读取中
                SetBufferStatus(activeBufferIndex, BUFFER_READING);

                // 读取数据
                data = new byte[readLength];
                var accessor = _bufferAccessors[activeBufferIndex];
                accessor.ReadArray(offset, data, 0, readLength);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取共享内存失败: {ex.Message}");
                return false;
            }
            finally
            {
                // 更新缓冲区状态为就绪（允许重复读取）
                SetBufferStatus(activeBufferIndex, BUFFER_READY);

                // 释放缓冲区锁
                ReleaseBufferLock(activeBufferIndex);
            }
        }

        public bool TryAcquireLock(int timeoutMs = Timeout.Infinite)
        {
            if (_state != SharedMemoryState.Created && _state != SharedMemoryState.Opened)
                throw new InvalidOperationException("共享内存未打开");

            int spinCount = 0;
            int maxSpinCount = 1000; // 最大自旋次数

            while (true)
            {
                // 尝试原子地获取锁
                if (Interlocked.CompareExchange(ref GetHeaderLock(), LOCK_TAKEN, LOCK_FREE) == LOCK_FREE)
                    return true;

                // 检查超时
                if (timeoutMs != Timeout.Infinite && spinCount++ > maxSpinCount)
                {
                    // 自旋一段时间后仍未获取到锁，进行线程让步
                    Thread.Sleep(1);

                    // 重新计算剩余超时时间
                    if (timeoutMs <= 0)
                        return false;
                }

                // 短时间等待，避免CPU占用过高
                Thread.SpinWait(100);
            }
        }

        public void ReleaseLock()
        {
            if (_state != SharedMemoryState.Created && _state != SharedMemoryState.Opened)
                throw new InvalidOperationException("共享内存未打开");

            // 释放锁（原子操作）
            Interlocked.Exchange(ref GetHeaderLock(), LOCK_FREE);
        }

        // 初始化头部信息
        private void InitializeHeader()
        {
            Interlocked.Exchange(ref GetHeaderActiveBufferIndex(), 0);
            Interlocked.Exchange(ref GetHeaderBufferStatus(0), BUFFER_EMPTY);
            Interlocked.Exchange(ref GetHeaderBufferStatus(1), BUFFER_EMPTY);
            Interlocked.Exchange(ref GetHeaderBufferDataLength(0), 0);
            Interlocked.Exchange(ref GetHeaderBufferDataLength(1), 0);
            Interlocked.Exchange(ref GetHeaderLock(), LOCK_FREE);
            Interlocked.Exchange(ref GetHeaderReferenceCount(), 1);
            Interlocked.Exchange(ref GetHeaderLastUpdateTimestamp(), DateTime.UtcNow.Ticks);
        }

        // 启动数据通知线程
        private void StartDataNotificationThread()
        {
            // 在线程池线程上启动数据监听
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    while (_state != SharedMemoryState.Closed && !_isDisposed)
                    {
                        // 等待数据可用事件
                        _dataAvailableEvent.Wait(1000);
                        _dataAvailableEvent.Reset();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"数据通知线程异常: {ex.Message}");
                }
            });
        }

        // 关闭内部资源
        private void CloseInternal()
        {
            try
            {
                // 减少引用计数
                int refCount = Interlocked.Decrement(ref GetHeaderReferenceCount());

                // 释放资源
                for (int i = 0; i < BUFFER_COUNT; i++)
                {
                    _bufferAccessors[i]?.Dispose();
                    _bufferAccessors[i] = null;
                }

                _headerAccessor?.Dispose();
                _headerAccessor = null;

                // 如果引用计数为0，关闭共享内存
                if (refCount <= 0 && _memoryMappedFile != null)
                {
                    _memoryMappedFile.Dispose();
                    _memoryMappedFile = null;
                }

                _dataAvailableEvent?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"关闭共享内存资源失败: {ex.Message}");
            }
        }

        // 通过非安全指针直接访问共享内存头部的ActiveBufferIndex字段
        // 返回该字段的引用（而非值拷贝），用于后续原子操作
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe ref int GetHeaderActiveBufferIndex()
        {
            // 1. 获取MemoryMappedViewAccessor的底层指针
            void* ptr = _headerAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle().ToPointer();

            // 2. 将指针转换为int引用（ActiveBufferIndex位于头部起始位置）
            return ref Unsafe.AsRef<int>(ptr);
        }

        // 通过非安全指针访问共享内存头部的BufferStatus字段
        // bufferIndex=0表示第一个缓冲区，=1表示第二个缓冲区
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe ref int GetHeaderBufferStatus(int bufferIndex)
        {
            void* ptr = _headerAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle().ToPointer();

            // 根据bufferIndex计算偏移量（按缓存行对齐）
            // 第一个缓冲区状态：偏移量 = 1个缓存行
            // 第二个缓冲区状态：偏移量 = 2个缓存行
            int offset = bufferIndex == 0 ? CACHE_LINE_SIZE : CACHE_LINE_SIZE * 2;

            // 返回指定缓冲区状态字段的引用
            return ref Unsafe.AsRef<int>((byte*)ptr + offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe ref int GetHeaderBufferDataLength(int bufferIndex)
        {
            void* ptr = _headerAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle().ToPointer();
            return ref Unsafe.AsRef<int>((byte*)ptr + (bufferIndex == 0 ? CACHE_LINE_SIZE * 3 : CACHE_LINE_SIZE * 4));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe ref int GetHeaderLock()
        {
            void* ptr = _headerAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle().ToPointer();
            return ref Unsafe.AsRef<int>((byte*)ptr + CACHE_LINE_SIZE * 5);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe ref int GetHeaderReferenceCount()
        {
            void* ptr = _headerAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle().ToPointer();
            return ref Unsafe.AsRef<int>((byte*)ptr + CACHE_LINE_SIZE * 6);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe ref long GetHeaderLastUpdateTimestamp()
        {
            void* ptr = _headerAccessor.SafeMemoryMappedViewHandle.DangerousGetHandle().ToPointer();
            return ref Unsafe.AsRef<long>((byte*)ptr + CACHE_LINE_SIZE * 7);
        }

        // 获取当前活动缓冲区索引
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetActiveBufferIndex() => Interlocked.CompareExchange(ref GetHeaderActiveBufferIndex(), 0, 0);

        // 获取缓冲区状态
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetBufferStatus(int bufferIndex) => Interlocked.CompareExchange(ref GetHeaderBufferStatus(bufferIndex), 0, 0);

        // 设置缓冲区状态
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetBufferStatus(int bufferIndex, int status) => Interlocked.Exchange(ref GetHeaderBufferStatus(bufferIndex), status);

        // 获取缓冲区数据长度
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetBufferDataLength(int bufferIndex) => Interlocked.CompareExchange(ref GetHeaderBufferDataLength(bufferIndex), 0, 0);

        // 设置缓冲区数据长度
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetBufferDataLength(int bufferIndex, int length) => Interlocked.Exchange(ref GetHeaderBufferDataLength(bufferIndex), length);

        // 设置最后更新时间戳
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetLastUpdateTimestamp(long timestamp) => Interlocked.Exchange(ref GetHeaderLastUpdateTimestamp(), timestamp);

        // 尝试获取缓冲区锁
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryAcquireBufferLock(int bufferIndex, int timeoutMs)
        {
            int spinCount = 0;
            int maxSpinCount = 1000;

            while (true)
            {
                // 尝试原子地获取锁
                if (Interlocked.CompareExchange(ref GetHeaderLock(), LOCK_TAKEN, LOCK_FREE) == LOCK_FREE)
                    return true;

                // 检查超时
                if (timeoutMs != Timeout.Infinite && spinCount++ > maxSpinCount)
                {
                    // 自旋一段时间后仍未获取到锁，进行线程让步
                    Thread.Sleep(1);

                    // 重新计算剩余超时时间
                    if (timeoutMs <= 0)
                        return false;
                }

                // 短时间等待，避免CPU占用过高
                Thread.SpinWait(100);
            }
        }

        // 释放缓冲区锁
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ReleaseBufferLock(int bufferIndex) => Interlocked.Exchange(ref GetHeaderLock(), LOCK_FREE);

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_isDisposed)
                    return;

                Close();
                _isDisposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}
