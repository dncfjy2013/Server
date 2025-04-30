using Protocol;
using Server.Client;
using Server.Core;
using Server.Logger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Server.Common
{
    // 处理客户端接收消息的线程管理器（适配ProcessMessages）
    public class IncomingMessageThreadManager : DynamicThreadManagerBase<ClientMessage>
    {
        private readonly DataPriority _priority;
        private readonly ServerInstance _server; // 添加 _server 字段

        public IncomingMessageThreadManager(
            ServerInstance server, // 构造函数添加 server 参数
            Channel<ClientMessage> channel,
            ILogger logger,
            DataPriority priority,
            int minThreads,
            int maxThreads,
            int queueThreshold = 100,
            int monitorIntervalMs = 1000)
            : base(channel, logger, minThreads, maxThreads, queueThreshold, monitorIntervalMs)
        {
            _server = server; // 初始化 _server 字段
            _priority = priority;
        }

        protected override Task ProcessMessageAsync(ClientMessage message, CancellationToken ct)
        {
            return _server.ProcessMessages(message.Data.Priority);
        }
    }
}
