using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Server.DataBase.NonRelateSQL;
using Server.DataBase.NonRelateSQL.Common;
using Server.DataBase.NonRelational;
using Server.DataBase.RelateSQL;

namespace Server.DataBase
{
    public class DatabaseFactory
    {
        // 创建数据库连接的方法
        public static IDatabaseConnection CreateRelateConnection(DatabaseConfig config)
        {
            switch (config.DatabaseType)
            {
                case DatabaseType.SqlServer:
                    return new SqlDatabaseConnection(config);
                case DatabaseType.MySql:
                    return new MySqlDatabaseConnection(config);
                case DatabaseType.PostgreSQL:
                    return new PostgreSQLDatabaseConnection(config);
                case DatabaseType.Oracle:
                    return new OracleDatabaseConnection(config);
                default:
                    throw new NotSupportedException($"Database type {config.DatabaseType} is not supported.");
            }
        }

        /// <summary>
        /// 创建非关系型数据库实例
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <param name="config">数据库配置</param>
        /// <returns>数据库操作实例</returns>
        public static INonRelationalDataset<T> CreateNonRelateConnection<T>(NonRelationalDatabaseConfig config) where T : class
        {
            if (!config.IsValid())
            {
                throw new ArgumentException("Invalid database configuration");
            }

            return config.DatabaseType switch
            {
                NonRelationalDatabaseType.Elasticsearch => CreateElasticsearch<T>(config),
                NonRelationalDatabaseType.MongoDB => CreateMongoDB<T>(config),
                NonRelationalDatabaseType.Redis => CreateRedis<T>(config),
                NonRelationalDatabaseType.Cassandra => CreateCassandra<T>(config),
                _ => throw new NotSupportedException($"Unsupported database type: {config.DatabaseType}")
            };
        }

        private static INonRelationalDataset<T> CreateElasticsearch<T>(NonRelationalDatabaseConfig config) where T : class
        {
            return new ElasticsearchDataset<T>(config);
        }

        private static INonRelationalDataset<T> CreateMongoDB<T>(NonRelationalDatabaseConfig config) where T : class
        {
            return new MongoDataset<T>(config);
        }

        private static INonRelationalDataset<T> CreateRedis<T>(NonRelationalDatabaseConfig config) where T : class
        {
            return new RedisDataset<T>(config);
        }

        private static INonRelationalDataset<T> CreateCassandra<T>(NonRelationalDatabaseConfig config) where T : class
        {
            return new CassandraDataset<T>(config);
        }
    }
}
