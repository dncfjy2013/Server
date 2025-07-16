using Entity.Communication.NetComu;
using Entity.Communication.USBComu.Common;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.USBComu
{
    // 基于WinUSB的实现
    public class WinUsbCommunication : UsbComuBase
    {
        private IntPtr _winUsbHandle = IntPtr.Zero;
        private IntPtr _deviceInterfaceHandle = IntPtr.Zero;

        // WinUSB相关常量和DLL导入
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Initialize(
            SafeFileHandle DeviceHandle,
            ref IntPtr InterfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Free(IntPtr InterfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ReadPipe(
            IntPtr InterfaceHandle,
            byte PipeID,
            byte[] Buffer,
            uint BufferLength,
            ref uint LengthTransferred,
            IntPtr Overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_WritePipe(
            IntPtr InterfaceHandle,
            byte PipeID,
            byte[] Buffer,
            uint BufferLength,
            ref uint LengthTransferred,
            IntPtr Overlapped);

        // 端点ID
        private byte _readEndpointId;
        private byte _writeEndpointId;

        public WinUsbCommunication(int vendorId, int productId, string serialNumber = null)
            : base(vendorId, productId, serialNumber)
        {
        }

        protected override async Task FindAndOpenDeviceAsync()
        {
            try
            {
                // 查找设备
                var devices = await GetAvailableDevicesAsync();
                var device = devices.FirstOrDefault(d =>
                    d.VendorId == VendorId &&
                    d.ProductId == ProductId &&
                    (string.IsNullOrEmpty(SerialNumber) || d.SerialNumber == SerialNumber));

                if (device == null)
                {
                    throw new DeviceNotFoundException($"未找到USB设备: VID={VendorId:X4}, PID={ProductId:X4}");
                }

                // 打开设备
                _deviceHandle = CreateFile(
                    device.DevicePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);

                if (_deviceHandle.IsInvalid)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    throw new IOException($"无法打开USB设备: 错误代码 {errorCode}");
                }

                // 初始化WinUSB
                if (!WinUsb_Initialize(_deviceHandle, ref _winUsbHandle))
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    _deviceHandle.Close();
                    throw new IOException($"无法初始化WinUSB: 错误代码 {errorCode}");
                }

                // 配置设备（在实际应用中，可能需要设置配置、接口和端点）
                ConfigureDevice();

                // 创建设备流
                _deviceStream = new FileStream(_deviceHandle, FileAccess.ReadWrite, ReceiveBufferSize, true);
            }
            catch (Exception ex)
            {
                CloseDevice();
                throw;
            }
        }

        private void ConfigureDevice()
        {
            // 在实际应用中，需要根据设备特性配置接口和端点
            // 这里是简化示例，实际实现需要根据具体设备进行调整

            // 例如：设置配置
            // WinUsb_SetCurrentConfiguration(_winUsbHandle, 1);

            // 例如：获取端点信息
            // _readEndpointId = ...;
            // _writeEndpointId = ...;

            // 本示例假设端点ID已预先知道
            _readEndpointId = 0x81;  // 输入端点
            _writeEndpointId = 0x01; // 输出端点
        }

        public override async Task<List<UsbDeviceInfo>> GetAvailableDevicesAsync()
        {
            // 实际实现需要使用Windows API枚举USB设备
            // 这是一个简化示例，实际代码会更复杂

            var devices = new List<UsbDeviceInfo>();

            // 示例：添加一个模拟设备
            devices.Add(new UsbDeviceInfo
            {
                DeviceId = "VID_1234&PID_5678",
                DevicePath = @"\\?\USB#VID_1234&PID_5678#ABC123456789",
                Description = "示例USB设备",
                Manufacturer = "制造商",
                Product = "产品名称",
                SerialNumber = "ABC123456789",
                VendorId = 0x1234,
                ProductId = 0x5678,
                Revision = 0x0100
            });

            return devices;
        }

        protected override async Task ProcessSendQueueAsync()
        {
            try
            {
                while (_sendQueue.TryDequeue(out byte[] data))
                {
                    if (_winUsbHandle == IntPtr.Zero)
                    {
                        OnErrorOccurred(UsbErrorType.DeviceNotOpen, "发送失败：设备未打开");
                        break;
                    }

                    try
                    {
                        uint bytesWritten = 0;
                        bool result = WinUsb_WritePipe(
                            _winUsbHandle,
                            _writeEndpointId,
                            data,
                            (uint)data.Length,
                            ref bytesWritten,
                            IntPtr.Zero);

                        if (!result || bytesWritten != data.Length)
                        {
                            int errorCode = Marshal.GetLastWin32Error();
                            throw new IOException($"写入USB设备失败: 错误代码 {errorCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(UsbErrorType.WriteFailed, $"发送数据失败: {ex.Message}", ex);
                        // 将数据放回队列重试
                        _sendQueue.Enqueue(data);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(UsbErrorType.WriteFailed, $"处理发送队列失败: {ex.Message}", ex);
            }
        }

        protected override void CloseDevice()
        {
            try
            {
                if (_winUsbHandle != IntPtr.Zero)
                {
                    WinUsb_Free(_winUsbHandle);
                    _winUsbHandle = IntPtr.Zero;
                }
            }
            finally
            {
                base.CloseDevice();
            }
        }
    }

    // 使用示例
    public class WinUsbProtocolExample
    {
        public static async Task Main(string[] args)
        {
            // 使用WinUSB通信示例
            using (WinUsbCommunication usbComm = new WinUsbCommunication(0x1234, 0x5678, "ABC123456789"))
            {
                usbComm.DataReceived += (sender, data) =>
                {
                    Console.WriteLine($"收到USB数据: {BitConverter.ToString(data)}");
                };

                usbComm.ErrorOccurred += (sender, e) =>
                {
                    Console.WriteLine($"USB通信错误: {e.Message}");
                };

                usbComm.StatusChanged += (sender, status) =>
                {
                    Console.WriteLine($"USB状态变更: {status}");
                };

                try
                {
                    await usbComm.OpenAsync();
                    Console.WriteLine("USB设备已连接");

                    // 发送数据
                    await usbComm.SendAsync(new byte[] { 0x01, 0x02, 0x03, 0x04 });

                    // 等待一段时间接收数据
                    await Task.Delay(5000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"操作失败: {ex.Message}");
                }
            }
        }
    }
}
