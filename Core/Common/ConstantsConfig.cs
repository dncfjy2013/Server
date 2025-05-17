using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Core.Common
{
    public class ConstantsConfig
    {
        public const bool IsDevelopment = true; 
        public const bool IsUnityServer = true;

        private static int _basenum = Environment.ProcessorCount / 2;

        #region in thread num
        public static int In_High_MinThreadNum = _basenum / 2;
        public static int In_High_MaxThreadNum = _basenum * 2;

        public static int In_Medium_MinThreadNum = _basenum / 4;
        public static int In_Medium_MaxThreadNum = _basenum;

        public static int In_Low_MinThreadNum = 1;
        public static int In_Low_MaxThreadNum = _basenum / 4;
        #endregion

        #region In Semaphores

        public static int High_Min_Semaphores = _basenum * 2;
        public static int High_Max_Semaphores = _basenum * 2;

        public static int Medium_Min_Semaphores = _basenum;
        public static int Medium_Max_Semaphores = _basenum;

        public static int Low_Min_Semaphores = _basenum / 2;
        public static int Low_Max_Semaphores = _basenum / 2;
        #endregion

        #region out thread num
        public static int Out_High_MinThreadNum = 0;
        public static int Out_High_MaxThreadNum = _basenum;

        public static int Out_Medium_MinThreadNum = 0;
        public static int Out_Medium_MaxThreadNum = _basenum / 2;

        public static int Out_Low_MinThreadNum = 0;
        public static int Out_Low_MaxThreadNum = _basenum / 4;
        #endregion

        // 流量监控的时间间隔，单位为毫秒，默认值为 5000 毫秒（即 5 秒），可通过相应方法修改
        public const int MonitorInterval = 5000;
        // 心跳检查的时间间隔，单位为毫秒，固定值为 10000 毫秒（即 10 秒）
        public const int HeartbeatInterval = 10000;
        // 套接字监听器的最大连接队列长度，即等待处理的客户端连接请求的最大数量，固定值为 100
        public const int ListenMax = 100;
        // 定义消息队列的最大容量，这里设置为 int 类型的最大值，表示队列理论上可以无限扩展
        public const int MaxQueueSize = int.MaxValue;
        // 心跳超时时间（秒），超过此时间未收到客户端活动则视为断开
        public const int TimeoutSeconds = 45;

        public const int In_Queue_ThreadshouldSize = 100;
        public const int Out_Queue_ThreadshouldSize = 100;

        public const int In_Queue_MonitorIntervalMs = 1000;
        public const int Out_Queue_MonitorIntervalMs = 1000;

        public const int Send_High_Retry = 5;
        public static readonly TimeSpan Send_High_Retry_TimeSpan = TimeSpan.FromSeconds(5);

        public const int Send_Medium_Retry = 3;
        public static readonly TimeSpan Send_Medium_Retry_TimeSpan = TimeSpan.FromSeconds(10);

        public const int Send_Low_Retry = 1;
        public static readonly TimeSpan Send_Low_Retry_TimeSpan = TimeSpan.FromSeconds(15);

        /// <summary>
        /// 协议全局配置（序列化、校验和、版本支持等）
        /// </summary>
        public static ProtocolConfiguration config = new ProtocolConfiguration
        {
            DataSerializer = new ProtobufSerializerAdapter(),        // Protobuf序列化器
            ChecksumCalculator = new Crc16Calculator(),              // CRC16校验和计算器
            SupportedVersions = new byte[] { 0x01, 0x02 },           // 支持的协议版本号
            MaxPacketSize = 128 * 1024 * 1024                        // 最大数据包大小（128MB）
        };

    }
}
