using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Entity.Communication.PLCComu.Common
{
    // PLC通讯异常类
    public class PlcCommunicationException : Exception
    {
        public PlcCommunicationException(string message) : base(message) { }
        public PlcCommunicationException(string message, Exception innerException) : base(message, innerException) { }
    }
}
