using System;
using System.IO.Ports;
using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Entity.Communication.ComComu.Common
{
    // 串口基类
    public abstract class SerialComuBase : IDisposable
    {
        #region 成员变量
        private readonly SerialPort _serialPort;
        private readonly ConcurrentQueue<byte[]> _receiveQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent _sendCompletedEvent = new ManualResetEvent(true);
        private readonly object _syncRoot = new object();
        private Thread _receiveThread;
        private Thread _sendThread;
        private IProtocol _protocol;
        private byte[] _receiveBuffer;
        private int _bufferPosition;
        private bool _isDisposed;
        #endregion

        #region 属性
        /// <summary>
        /// 串口名称
        /// </summary>
        public string PortName { get; }

        /// <summary>
        /// 波特率
        /// </summary>
        public int BaudRate { get; }

        /// <summary>
        /// 数据位
        /// </summary>
        public int DataBits { get; }

        /// <summary>
        /// 停止位
        /// </summary>
        public StopBits StopBits { get; }

        /// <summary>
        /// 校验位
        /// </summary>
        public Parity Parity { get; }

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
        /// 当前连接状态
        /// </summary>
        public bool IsConnected => _serialPort.IsOpen;

        /// <summary>
        /// 当前使用的协议
        /// </summary>
        public IProtocol Protocol
        {
            get => _protocol;
            set
            {
                lock (_syncRoot)
                {
                    _protocol = value;
                    ClearReceiveBuffer();
                }
            }
        }
        #endregion

        #region 事件
        /// <summary>
        /// 接收到数据事件
        /// </summary>
        public event EventHandler<byte[]> DataReceived;

        /// <summary>
        /// 错误发生事件
        /// </summary>
        public event EventHandler<SerialErrorEventArgs> ErrorOccurred;

        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event EventHandler<bool> ConnectionStatusChanged;
        #endregion

        #region 构造函数
        protected SerialComuBase(string portName, int baudRate = 9600,
            Parity parity = Parity.None, int dataBits = 8,
            StopBits stopBits = StopBits.One)
        {
            PortName = portName;
            BaudRate = baudRate;
            DataBits = dataBits;
            StopBits = stopBits;
            Parity = parity;

            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
            _receiveBuffer = new byte[ReceiveBufferSize];
        }
        #endregion

        #region 公共方法
        /// <summary>
        /// 打开串口连接
        /// </summary>
        public virtual void Open()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (IsConnected)
                return;

            try
            {
                _serialPort.Open();
                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.ErrorReceived += SerialPort_ErrorReceived;

                // 启动接收和发送线程
                _receiveThread = new Thread(ReceiveProcessingLoop) { IsBackground = true };
                _sendThread = new Thread(SendProcessingLoop) { IsBackground = true };

                _receiveThread.Start();
                _sendThread.Start();

                OnConnectionStatusChanged(true);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.CommunicationError, $"打开串口失败: {ex.Message}", ex);
                Close();
                throw;
            }
        }

        /// <summary>
        /// 关闭串口连接
        /// </summary>
        public virtual void Close()
        {
            if (_isDisposed)
                return;

            try
            {
                _cancellationTokenSource.Cancel();

                _serialPort.DataReceived -= SerialPort_DataReceived;
                _serialPort.ErrorReceived -= SerialPort_ErrorReceived;

                if (IsConnected)
                {
                    _serialPort.Close();
                    OnConnectionStatusChanged(false);
                }

                // 等待线程结束
                _receiveThread?.Join(500);
                _sendThread?.Join(500);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.CommunicationError, $"关闭串口失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public virtual void Send(byte[] data)
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);

            if (data == null || data.Length == 0)
                return;

            try
            {
                // 如果有协议，使用协议编码
                byte[] encodedData = Protocol?.EncodeMessage(data) ?? data;
                _sendQueue.Enqueue(encodedData);
                _sendCompletedEvent.Reset();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.CommunicationError, $"添加到发送队列失败: {ex.Message}", ex);
            }
        }

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
        /// 处理接收到的数据 - 可由子类重写
        /// </summary>
        protected virtual void ProcessReceivedData(byte[] data)
        {
            try
            {
                DataReceived?.Invoke(this, data);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.ProtocolError, $"处理接收数据失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 当发生错误时调用
        /// </summary>
        protected virtual void OnErrorOccurred(SerialErrorType errorType, string message, Exception innerException = null)
        {
            try
            {
                ErrorOccurred?.Invoke(this, new SerialErrorEventArgs(errorType, message, innerException));

                // 如果启用自动重连，尝试重新连接
                if (AutoReconnect && errorType == SerialErrorType.CommunicationError && IsConnected)
                {
                    Reconnect();
                }
            }
            catch (Exception ex)
            {
                // 防止错误处理中再次出错
                Console.WriteLine($"错误处理失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 当连接状态变化时调用
        /// </summary>
        protected virtual void OnConnectionStatusChanged(bool isConnected)
        {
            try
            {
                ConnectionStatusChanged?.Invoke(this, isConnected);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.CommunicationError, $"触发连接状态变化事件失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 尝试重新连接
        /// </summary>
        protected virtual void Reconnect()
        {
            try
            {
                Close();
                Thread.Sleep(500); // 等待一段时间再尝试
                Open();
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.CommunicationError, $"重新连接失败: {ex.Message}", ex);
            }
        }
        #endregion

        #region 私有方法
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (!IsConnected)
                    return;

                int bytesToRead = _serialPort.BytesToRead;
                if (bytesToRead <= 0)
                    return;

                // 确保有足够的缓冲区空间
                if (_bufferPosition + bytesToRead > _receiveBuffer.Length)
                {
                    // 扩展缓冲区
                    Array.Resize(ref _receiveBuffer, _receiveBuffer.Length * 2);
                }

                // 读取数据
                _serialPort.Read(_receiveBuffer, _bufferPosition, bytesToRead);
                _bufferPosition += bytesToRead;

                // 通知接收线程有新数据
                Monitor.PulseAll(_syncRoot);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.CommunicationError, $"读取串口数据失败: {ex.Message}", ex);
            }
        }

        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            OnErrorOccurred(SerialErrorType.CommunicationError, $"串口错误: {e.EventType}");
        }

        private void ReceiveProcessingLoop()
        {
            try
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        if (_bufferPosition <= 0)
                        {
                            // 没有数据，等待
                            lock (_syncRoot)
                            {
                                Monitor.Wait(_syncRoot, 100);
                            }
                            continue;
                        }

                        // 如果有协议，使用协议解析
                        if (Protocol != null)
                        {
                            ParseStatus status;
                            do
                            {
                                status = ParseProtocolMessage();
                            } while (status == ParseStatus.Complete && _bufferPosition > 0);
                        }
                        else
                        {
                            // 没有协议，直接处理所有数据
                            byte[] data = new byte[_bufferPosition];
                            Array.Copy(_receiveBuffer, 0, data, 0, _bufferPosition);
                            ProcessReceivedData(data);
                            _bufferPosition = 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(SerialErrorType.ProtocolError, $"处理接收缓冲区失败: {ex.Message}", ex);
                        // 清空缓冲区，防止死循环
                        _bufferPosition = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.CommunicationError, $"接收处理线程异常: {ex.Message}", ex);
            }
        }

        private ParseStatus ParseProtocolMessage()
        {
            if (_bufferPosition <= 0)
                return ParseStatus.Incomplete;

            try
            {
                int messageLength = Protocol.GetMessageLength(_receiveBuffer.Take(_bufferPosition).ToArray());

                if (messageLength <= 0)
                    return ParseStatus.Incomplete;

                if (messageLength > _bufferPosition)
                    return ParseStatus.Incomplete;

                byte[] message = new byte[messageLength];
                Array.Copy(_receiveBuffer, 0, message, 0, messageLength);

                if (Protocol.ValidateMessage(message))
                {
                    byte[] decodedMessage = Protocol.DecodeMessage(message);
                    ProcessReceivedData(decodedMessage);

                    // 从缓冲区移除已处理的数据
                    if (messageLength < _bufferPosition)
                    {
                        Array.Copy(_receiveBuffer, messageLength, _receiveBuffer, 0, _bufferPosition - messageLength);
                    }
                    _bufferPosition -= messageLength;

                    return ParseStatus.Complete;
                }
                else
                {
                    // 无效消息，移除第一个字节继续尝试
                    Array.Copy(_receiveBuffer, 1, _receiveBuffer, 0, _bufferPosition - 1);
                    _bufferPosition--;

                    OnErrorOccurred(SerialErrorType.ProtocolError, "接收到无效的协议消息");
                    return ParseStatus.Invalid;
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.ProtocolError, $"解析协议消息失败: {ex.Message}", ex);
                // 清空缓冲区，防止死循环
                _bufferPosition = 0;
                return ParseStatus.Invalid;
            }
        }

        private void SendProcessingLoop()
        {
            try
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        if (!IsConnected)
                        {
                            Thread.Sleep(100);
                            continue;
                        }

                        if (_sendQueue.TryDequeue(out byte[] data))
                        {
                            _serialPort.Write(data, 0, data.Length);
                        }
                        else
                        {
                            // 队列为空，设置事件并等待
                            _sendCompletedEvent.Set();
                            Thread.Sleep(10);
                        }
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(SerialErrorType.CommunicationError, $"发送数据失败: {ex.Message}", ex);
                        // 发生错误时暂停发送
                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(SerialErrorType.CommunicationError, $"发送处理线程异常: {ex.Message}", ex);
            }
        }

        private void ClearReceiveBuffer()
        {
            lock (_syncRoot)
            {
                _bufferPosition = 0;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    Close();
                    _cancellationTokenSource.Dispose();
                    _sendCompletedEvent.Dispose();
                    _serialPort.Dispose();
                }

                _isDisposed = true;
            }
        }
        #endregion
    }
}
