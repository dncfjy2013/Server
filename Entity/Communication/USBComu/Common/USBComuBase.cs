using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Entity.Communication.USBComu.Common
{
    // USB通信基类
    public abstract class UsbComuBase : IDisposable
    {
        #region 成员变量
        protected readonly int _vendorId;
        protected readonly int _productId;
        protected readonly string _serialNumber;
        protected SafeFileHandle _deviceHandle;
        protected FileStream _deviceStream;
        protected Thread _receiveThread;
        protected readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        protected readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        protected UsbCommunicationStatus _status = UsbCommunicationStatus.Disconnected;
        protected bool _isDisposed;
        #endregion

        #region 属性
        /// <summary>
        /// 设备厂商ID
        /// </summary>
        public int VendorId => _vendorId;

        /// <summary>
        /// 设备产品ID
        /// </summary>
        public int ProductId => _productId;

        /// <summary>
        /// 设备序列号
        /// </summary>
        public string SerialNumber => _serialNumber;

        /// <summary>
        /// 当前连接状态
        /// </summary>
        public UsbCommunicationStatus Status => _status;

        /// <summary>
        /// 接收缓冲区大小
        /// </summary>
        public int ReceiveBufferSize { get; set; } = 4096;

        /// <summary>
        /// 发送超时时间(毫秒)
        /// </summary>
        public int SendTimeout { get; set; } = 1000;

        /// <summary>
        /// 接收超时时间(毫秒)
        /// </summary>
        public int ReceiveTimeout { get; set; } = 2000;

        /// <summary>
        /// 是否启用自动重连
        /// </summary>
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// 自动重连间隔(毫秒)
        /// </summary>
        public int ReconnectInterval { get; set; } = 5000;
        #endregion

        #region 事件
        /// <summary>
        /// 接收到数据事件
        /// </summary>
        public event EventHandler<byte[]> DataReceived;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event EventHandler<UsbErrorEventArgs> ErrorOccurred;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<UsbCommunicationStatus> StatusChanged;

        /// <summary>
        /// 设备列表变化事件
        /// </summary>
        public event EventHandler<List<UsbDeviceInfo>> DeviceListChanged;
        #endregion

        #region 构造函数
        protected UsbComuBase(int vendorId, int productId, string serialNumber = null)
        {
            _vendorId = vendorId;
            _productId = productId;
            _serialNumber = serialNumber;
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 打开USB设备连接
        /// </summary>
        public virtual async Task OpenAsync()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (Status == UsbCommunicationStatus.Connected || Status == UsbCommunicationStatus.Connecting)
                return;

            try
            {
                SetStatus(UsbCommunicationStatus.Connecting);

                // 查找并打开设备
                await FindAndOpenDeviceAsync();

                // 启动接收线程
                StartReceiveThread();

                SetStatus(UsbCommunicationStatus.Connected);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.DeviceNotFound, $"打开USB设备失败: {ex.Message}", ex);
                SetStatus(UsbCommunicationStatus.Disconnected);
                throw;
            }
        }

        /// <summary>
        /// 关闭USB设备连接
        /// </summary>
        public virtual async Task CloseAsync()
        {
            if (_isDisposed)
                return;

            try
            {
                if (Status == UsbCommunicationStatus.Disconnected || Status == UsbCommunicationStatus.Disconnecting)
                    return;

                SetStatus(UsbCommunicationStatus.Disconnecting);

                // 停止接收线程
                StopReceiveThread();

                // 关闭设备流和句柄
                CloseDevice();

                SetStatus(UsbCommunicationStatus.Disconnected);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.DeviceNotOpen, $"关闭USB设备失败: {ex.Message}", ex);
                SetStatus(UsbCommunicationStatus.Disconnected);
                throw;
            }
        }

        /// <summary>
        /// 发送数据到USB设备
        /// </summary>
        public virtual async Task SendAsync(byte[] data)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (data == null || data.Length == 0)
                return;

            if (Status != UsbCommunicationStatus.Connected)
                throw new InvalidOperationException("无法发送数据：USB设备未连接");

            try
            {
                _sendQueue.Enqueue(data);
                await ProcessSendQueueAsync();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.WriteFailed, $"添加到发送队列失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 获取所有可用的USB设备列表
        /// </summary>
        public abstract Task<List<UsbDeviceInfo>> GetAvailableDevicesAsync();

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion

        #region 保护方法
        /// <summary>
        /// 查找并打开USB设备
        /// </summary>
        protected abstract Task FindAndOpenDeviceAsync();

        /// <summary>
        /// 发送队列处理
        /// </summary>
        protected abstract Task ProcessSendQueueAsync();

        /// <summary>
        /// 启动接收线程
        /// </summary>
        protected virtual void StartReceiveThread()
        {
            if (_receiveThread != null && _receiveThread.IsAlive)
                return;

            _receiveThread = new Thread(ReceiveDataLoop);
            _receiveThread.IsBackground = true;
            _receiveThread.Start();
        }

        /// <summary>
        /// 停止接收线程
        /// </summary>
        protected virtual void StopReceiveThread()
        {
            try
            {
                _cancellationTokenSource.Cancel();
                _receiveThread?.Abort();
                _receiveThread = null;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.DeviceNotOpen, $"停止接收线程失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 接收数据循环
        /// </summary>
        protected virtual void ReceiveDataLoop()
        {
            try
            {
                byte[] buffer = new byte[ReceiveBufferSize];

                while (!_cancellationTokenSource.IsCancellationRequested && _deviceStream != null && _deviceStream.CanRead)
                {
                    try
                    {
                        if (_deviceStream.Length == 0)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        int bytesRead = _deviceStream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            byte[] data = new byte[bytesRead];
                            Array.Copy(buffer, 0, data, 0, bytesRead);
                            ProcessReceivedData(data);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 操作被取消，正常退出
                        break;
                    }
                    catch (IOException ex)
                    {
                        OnErrorOccurred(UsbErrorType.ReadFailed, $"读取USB数据失败: {ex.Message}", ex);

                        // 如果是连接断开错误，尝试重新连接
                        if (IsConnectionError(ex))
                        {
                            CloseAsync().Wait();

                            if (AutoReconnect)
                            {
                                ReconnectAsync();
                            }

                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(UsbErrorType.ReadFailed, $"接收数据时发生未知错误: {ex.Message}", ex);
                    }
                }
            }
            catch (ThreadAbortException)
            {
                // 线程被中止，正常关闭
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.ReadFailed, $"接收线程异常: {ex.Message}", ex);
            }
            finally
            {
                // 确保连接关闭
                CloseAsync().Wait();
            }
        }

        /// <summary>
        /// 判断是否为连接错误
        /// </summary>
        protected virtual bool IsConnectionError(Exception ex)
        {
            // 检查是否是设备断开连接的错误
            string message = ex.Message.ToLower();
            return message.Contains("disconnected") ||
                   message.Contains("not open") ||
                   message.Contains("closed");
        }

        /// <summary>
        /// 处理接收到的数据
        /// </summary>
        protected virtual void ProcessReceivedData(byte[] data)
        {
            try
            {
                DataReceived?.Invoke(this, data);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.ProtocolError, $"处理接收数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 关闭设备
        /// </summary>
        protected virtual void CloseDevice()
        {
            try
            {
                _deviceStream?.Close();
                _deviceStream = null;

                _deviceHandle?.Close();
                _deviceHandle = null;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.DeviceNotOpen, $"关闭设备失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 当发生错误时调用
        /// </summary>
        protected virtual void OnErrorOccurred(UsbErrorType errorType, string message, Exception innerException = null)
        {
            try
            {
                ErrorOccurred?.Invoke(this, new UsbErrorEventArgs(errorType, message, innerException));

                // 如果启用自动重连，尝试重新连接
                if (AutoReconnect &&
                    (errorType == UsbErrorType.DeviceNotFound ||
                     errorType == UsbErrorType.DeviceDisconnected ||
                     errorType == UsbErrorType.ReadFailed))
                {
                    ReconnectAsync();
                }
            }
            catch (Exception ex)
            {
                // 防止错误处理中再次出错
                Console.WriteLine($"错误处理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置连接状态
        /// </summary>
        protected virtual void SetStatus(UsbCommunicationStatus newStatus)
        {
            if (_status != newStatus)
            {
                _status = newStatus;
                StatusChanged?.Invoke(this, newStatus);
            }
        }

        /// <summary>
        /// 尝试重新连接
        /// </summary>
        protected async Task ReconnectAsync()
        {
            try
            {
                // 确保已断开
                await CloseAsync();

                // 等待一段时间
                await Task.Delay(ReconnectInterval);

                // 尝试重新连接
                await OpenAsync();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.DeviceNotFound, $"重新连接失败: {ex.Message}", ex);
            }
        }
        #endregion

        #region 释放资源
        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    CloseAsync().Wait();
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }

                _isDisposed = true;
            }
        }
        #endregion
    }

}
