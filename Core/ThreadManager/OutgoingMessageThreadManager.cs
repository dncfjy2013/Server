﻿using Protocol;
using Server.Core.Common;
using Server.Logger;
using System.Threading.Channels;

namespace Server.Core.ThreadManager
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
            int queueThreshold = ConstantsConfig.Out_Queue_ThreadshouldSize,
            int monitorIntervalMs = ConstantsConfig.Out_Queue_MonitorIntervalMs,
            string name = "OutComing")
            : base(channel, logger, minThreads, maxThreads, queueThreshold, monitorIntervalMs, name, priority.ToString())
        {
            _server = server;
            _priority = priority;
        }

        protected override Task ProcessMessageAsync(ServerOutgoingMessage msg, CancellationToken ct)
        {
            return _server.ProcessOutgoingMessages(msg, ct);
        }
    }
}
