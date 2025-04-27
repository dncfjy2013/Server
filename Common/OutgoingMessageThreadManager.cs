using Protocol;
using Server.Core;
using Server.Extend;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Server.Common
{
    // 处理服务器主动消息的线程管理器（适配ProcessOutgoingMessages）
    public class OutgoingMessageThreadManager : DynamicThreadManagerBase<ServerOutgoingMessage>
    {
        private readonly ServerInstance _server;
        private readonly DataPriority _priority;

        public OutgoingMessageThreadManager(
            ServerInstance server,
            Channel<ServerOutgoingMessage> channel,
            ILogger logger,
            DataPriority priority,
            int minThreads,
            int maxThreads,
            int queueThreshold = 100,
            int monitorIntervalMs = 1000)
            : base(channel, logger, minThreads, maxThreads, queueThreshold, monitorIntervalMs)
        {
            _server = server;
            _priority = priority;
        }

        protected override Task ProcessMessageAsync(ServerOutgoingMessage msg, CancellationToken ct)
        {
            return _server.ProcessOutgoingMessages(_channel, _priority);
        }
    }
}
