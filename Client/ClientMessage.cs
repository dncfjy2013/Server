using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Client
{
    public class ClientMessage
    {
        public ClientConfig Client { get; set; }
        public CommunicationData Data { get; set; }
        public DateTime ReceivedTime { get; set; }
    }
}
