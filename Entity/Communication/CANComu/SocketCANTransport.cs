using Entity.Communication.BluetoothComu.Common;
using Entity.Communication.BluetoothComu;
using Entity.Communication.CANComu.Common;
using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Entity.Communication.CANComu
{
    // 基于SocketCAN的Linux实现
    public class SocketCANTransport : CANTransportBase
    {
        // SocketCAN结构定义
        private const int PF_CAN = 29;
        private const int SOCK_RAW = 3;
        private const int CAN_RAW = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct sockaddr_can
        {
            public int can_family;
            public int can_ifindex;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] can_addr;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct can_frame
        {
            public uint can_id;
            public byte can_dlc;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] data;
        }

        private int _socket;
        private Task _receiveTask;
        private CancellationTokenSource _receiveCts;

        protected override async Task<bool> ConnectCoreAsync(CANConnectionParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                // 创建Socket
                _socket = SocketCANNative.socket(PF_CAN, SOCK_RAW, CAN_RAW);
                if (_socket < 0)
                {
                    throw new IOException($"创建CAN Socket失败: {Marshal.GetLastWin32Error()}");
                }

                // 获取网络接口索引
                int ifIndex = SocketCANNative.if_nametoindex(parameters.Channel);
                if (ifIndex == 0)
                {
                    throw new ArgumentException($"找不到CAN通道: {parameters.Channel}");
                }

                // 设置Socket地址
                var addr = new sockaddr_can
                {
                    can_family = PF_CAN,
                    can_ifindex = ifIndex,
                    can_addr = new byte[16]
                };

                // 绑定Socket
                int result = SocketCANNative.bind(_socket, ref addr, Marshal.SizeOf(addr));
                if (result < 0)
                {
                    throw new IOException($"绑定CAN Socket失败: {Marshal.GetLastWin32Error()}");
                }

                // 启动接收任务
                StartReceiveTask();

                return true;
            }
            catch (Exception ex)
            {
                // 关闭Socket（如果已创建）
                if (_socket >= 0)
                {
                    SocketCANNative.close(_socket);
                    _socket = -1;
                }

                Console.WriteLine($"连接CAN总线失败: {ex.Message}");
                return false;
            }
        }

        private void StartReceiveTask()
        {
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(_connectionCts.Token);

            _receiveTask = Task.Run(async () =>
            {
                try
                {
                    while (!_receiveCts.Token.IsCancellationRequested && _socket >= 0)
                    {
                        try
                        {
                            var frame = ReceiveFrame();
                            if (frame != null)
                            {
                                var message = ConvertToCANMessage((can_frame)frame);
                                OnMessageReceived(message);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // 正常取消
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"接收CAN帧时发生错误: {ex.Message}");

                            // 发生错误时尝试重新连接
                            await TryReconnect();

                            // 短暂延迟后继续尝试接收
                            await Task.Delay(1000);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CAN接收任务异常退出: {ex.Message}");
                }
            }, _receiveCts.Token);
        }

        private can_frame? ReceiveFrame()
        {
            if (_socket < 0)
                return null;

            var frame = new can_frame();
            int size = Marshal.SizeOf(frame);
            IntPtr buffer = Marshal.AllocHGlobal(size);

            try
            {
                // 从Socket读取数据
                int bytesRead = SocketCANNative.read(_socket, buffer, size);
                if (bytesRead <= 0)
                {
                    return null;
                }

                // 将数据转换为结构体
                frame = (can_frame)Marshal.PtrToStructure(buffer, typeof(can_frame));
                return frame;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private CANMessage ConvertToCANMessage(can_frame frame)
        {
            return new CANMessage
            {
                Id = frame.can_id & 0x1FFFFFFF, // 清除标志位
                IsExtendedFrame = (frame.can_id & 0x80000000) != 0,
                IsRemoteFrame = (frame.can_id & 0x40000000) != 0,
                Data = frame.data,
                Timestamp = DateTime.Now
            };
        }

        protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                // 取消接收任务
                _receiveCts?.Cancel();
                _receiveTask?.Wait(cancellationToken);

                // 关闭Socket
                if (_socket >= 0)
                {
                    SocketCANNative.close(_socket);
                    _socket = -1;
                }

                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"断开CAN连接时发生错误: {ex.Message}");
                return Task.CompletedTask;
            }
        }

        protected override Task<bool> SendMessageCoreAsync(CANMessage message, CancellationToken cancellationToken)
        {
            try
            {
                if (_socket < 0)
                    return Task.FromResult(false);

                var frame = ConvertToCanFrame(message);
                int size = Marshal.SizeOf(frame);
                IntPtr buffer = Marshal.AllocHGlobal(size);

                try
                {
                    // 将结构体复制到缓冲区
                    Marshal.StructureToPtr(frame, buffer, true);

                    // 发送数据
                    int bytesSent = SocketCANNative.write(_socket, buffer, size);
                    return Task.FromResult(bytesSent == size);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送CAN消息时发生错误: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private can_frame ConvertToCanFrame(CANMessage message)
        {
            uint canId = message.Id;
            if (message.IsExtendedFrame)
                canId |= 0x80000000;
            if (message.IsRemoteFrame)
                canId |= 0x40000000;

            return new can_frame
            {
                can_id = canId,
                can_dlc = (byte)Math.Min(message.Data.Length, 8),
                data = message.Data
            };
        }

        protected override Task<CANMessage> ReceiveMessageCoreAsync(CancellationToken cancellationToken)
        {
            // 注意：此方法在消息接收任务中被调用
            // 这里使用阻塞方式接收消息
            var frame = ReceiveFrame();
            return Task.FromResult(frame.HasValue ? ConvertToCANMessage(frame.Value) : null);
        }

        protected override Task SetFilterCoreAsync(uint id, uint mask, bool isExtended, CancellationToken cancellationToken)
        {
            // 设置CAN过滤器的实现
            // 简化示例，实际实现需要使用setsockopt等系统调用

            Console.WriteLine($"设置CAN过滤器 - ID: 0x{id:X}, Mask: 0x{mask:X}, Extended: {isExtended}");
            return Task.CompletedTask;
        }

        // SocketCAN Native方法
        private static class SocketCANNative
        {
            [DllImport("libc", SetLastError = true)]
            public static extern int socket(int domain, int type, int protocol);

            [DllImport("libc", SetLastError = true)]
            public static extern int bind(int sockfd, ref sockaddr_can addr, int addrlen);

            [DllImport("libc", SetLastError = true)]
            public static extern int close(int fd);

            [DllImport("libc", SetLastError = true)]
            public static extern int read(int fd, IntPtr buf, int count);

            [DllImport("libc", SetLastError = true)]
            public static extern int write(int fd, IntPtr buf, int count);

            [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi)]
            public static extern int if_nametoindex(string ifname);
        }
    }

    // 使用示例
    public class SocketCANTest
    {
        private static ICANTransport _canTransport;
        private static readonly ManualResetEvent _connectionEvent = new ManualResetEvent(false);
        private static readonly ManualResetEvent _disconnectionEvent = new ManualResetEvent(false);
        private static readonly AutoResetEvent _messageReceivedEvent = new AutoResetEvent(false);

        public static async Task Main()
        {
            try
            {
                Console.WriteLine("CAN总线通信示例程序启动...");

                // 创建CAN传输实例（这里使用SocketCAN实现，实际应用中可能需要其他实现）
                _canTransport = new SocketCANTransport();

                // 注册事件处理程序
                _canTransport.ConnectionStateChanged += OnConnectionStateChanged;
                _canTransport.MessageReceived += OnMessageReceived;

                // 准备连接参数
                var connectionParams = new CANConnectionParameters
                {
                    Channel = "can0",  // 替换为实际CAN通道名称
                    BaudRate = 500000  // 500 kbps
                };

                // 连接到CAN总线
                Console.WriteLine($"正在连接到CAN总线: {connectionParams.Channel}");
                bool connected = await _canTransport.ConnectAsync(connectionParams);

                if (connected)
                {
                    Console.WriteLine("已成功连接到CAN总线");

                    // 等待连接建立完成
                    _connectionEvent.WaitOne();

                    // 设置消息过滤器（可选）
                    await _canTransport.SetFilterAsync(0x100, 0x7FF);
                    Console.WriteLine("已设置CAN消息过滤器");

                    // 发送CAN消息
                    var message = new CANMessage
                    {
                        Id = 0x123,
                        IsExtendedFrame = false,
                        Data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }
                    };

                    Console.WriteLine($"准备发送CAN消息: {message}");
                    bool sent = await _canTransport.SendMessageAsync(message);

                    if (sent)
                    {
                        Console.WriteLine("CAN消息发送成功");
                    }
                    else
                    {
                        Console.WriteLine("CAN消息发送失败");
                    }

                    // 等待接收消息
                    Console.WriteLine("等待接收CAN消息...");
                    _messageReceivedEvent.WaitOne(5000);

                    // 保持程序运行一段时间，观察可能的异步消息接收
                    Console.WriteLine("按任意键断开连接并退出...");
                    Console.ReadKey();
                }
                else
                {
                    Console.WriteLine("连接失败");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
            }
            finally
            {
                // 断开连接并释放资源
                if (_canTransport != null)
                {
                    Console.WriteLine("正在断开CAN连接...");
                    await _canTransport.DisconnectAsync();
                    _canTransport.Dispose();
                    Console.WriteLine("已断开连接并释放资源");
                }

                // 清理事件
                _connectionEvent.Dispose();
                _disconnectionEvent.Dispose();
                _messageReceivedEvent.Dispose();
            }
        }

        private static void OnConnectionStateChanged(object sender, CANConnectionStateChangedEventArgs e)
        {
            Console.WriteLine($"CAN连接状态变更: {e.NewState}");
            if (!string.IsNullOrEmpty(e.Message))
            {
                Console.WriteLine($"消息: {e.Message}");
            }
            if (e.Error != null)
            {
                Console.WriteLine($"错误: {e.Error.Message}");
            }

            if (e.NewState == CANConnectionState.Connected)
            {
                _connectionEvent.Set();
            }
            else if (e.NewState == CANConnectionState.Disconnected)
            {
                _disconnectionEvent.Set();
            }
        }

        private static void OnMessageReceived(object sender, CANMessageReceivedEventArgs e)
        {
            try
            {
                Console.WriteLine($"接收到CAN消息: {e.Message}");
                _messageReceivedEvent.Set();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理CAN消息时发生错误: {ex.Message}");
            }
        }

    }

}

