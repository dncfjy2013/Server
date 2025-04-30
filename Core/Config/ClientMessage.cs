using Protocol;

namespace Server.Core.Config
{
    public class ClientMessage
    {
        public ClientConfig Client { get; set; }
        public CommunicationData Data { get; set; }
        public DateTime ReceivedTime { get; set; }
    }
}
