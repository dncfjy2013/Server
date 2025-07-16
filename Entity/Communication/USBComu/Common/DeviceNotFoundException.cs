using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.USBComu.Common
{
    // 设备未找到异常
    public class DeviceNotFoundException : Exception
    {
        public DeviceNotFoundException(string message) : base(message)
        {
        }
    }
}
