using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.WiFiComu.Common
{
    // WiFi连接参数
    public class WiFiConnectionParameters
    {
        public string SSID { get; set; }
        public string Password { get; set; }
        public bool IsSecure { get; set; } = true;
        public Dictionary<string, string> AdditionalParameters { get; set; } = new Dictionary<string, string>();
    }
}
