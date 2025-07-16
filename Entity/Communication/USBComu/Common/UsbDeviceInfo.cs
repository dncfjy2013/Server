using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.USBComu.Common
{
    // USB设备信息
    public class UsbDeviceInfo
    {
        public string DeviceId { get; set; }
        public string DevicePath { get; set; }
        public string Description { get; set; }
        public string Manufacturer { get; set; }
        public string Product { get; set; }
        public string SerialNumber { get; set; }
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public int Revision { get; set; }

        public override string ToString()
        {
            return $"{Description} (VID:{VendorId:X4} PID:{ProductId:X4})";
        }
    }
}
