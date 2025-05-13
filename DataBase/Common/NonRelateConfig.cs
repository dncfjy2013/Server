using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.DataBase.Common
{
    // 定义非关系型数据库类型枚举
    public enum NonRelationalDatabaseType
    {
        Redis,
        MongoDB,
        Cassandra,
        Elasticsearch,
        // 可以按需添加更多数据库类型
    }

    // 通用的非关系型数据库配置类
    public class NonRelationalDatabaseConfig : BaseConfig
    {
        // 数据库类型
        public NonRelationalDatabaseType DatabaseType { get; set; }

        // 通用配置
        public string Host { get; set; } = "localhost";
        public int Port { get; set; }
        public string DatabaseName { get; set; } = "";

        // 认证配置
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";

        // Redis 特定配置
        public int RedisDatabaseIndex { get; set; } = 0;
        public int RedisConnectTimeout { get; set; } = 5000;
        public int RedisSyncTimeout { get; set; } = 5000;

        // MongoDB 特定配置
        public string MongoDBReplicaSet { get; set; } = "";
        public bool MongoDBSSL { get; set; } = false;
        public bool MongoDBSSLAllowInvalidCertificates { get; set; } = false;

        // Cassandra 特定配置
        public int CassandraProtocolVersion { get; set; } = 4;
        public int CassandraConnectTimeoutMillis { get; set; } = 5000;

        // Elasticsearch 特定配置
        public string ElasticsearchApiKey { get; set; } = "";
        public string ElasticsearchCloudId { get; set; } = "";

        // 其他扩展配置
        public Dictionary<string, string> ExtendedProperties { get; set; } = new Dictionary<string, string>();

        // 验证配置是否有效
        public bool IsValid()
        {
            switch (DatabaseType)
            {
                case NonRelationalDatabaseType.Redis:
                    return !string.IsNullOrEmpty(Host) && Port > 0;
                case NonRelationalDatabaseType.MongoDB:
                    return !string.IsNullOrEmpty(Host) && Port > 0;
                case NonRelationalDatabaseType.Cassandra:
                    return !string.IsNullOrEmpty(Host) && Port > 0;
                case NonRelationalDatabaseType.Elasticsearch:
                    return !string.IsNullOrEmpty(Host) && Port > 0;
                default:
                    return false;
            }
        }
    }
}
