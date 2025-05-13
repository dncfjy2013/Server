using System.Reflection;
using Server.DataBase.Common;
using Server.DataBase.Core.NonRelateSQL;
using Server.DataBase.Core.RelateSQL;

namespace Server.DataBase.Core
{
    public class DatabaseFactory
    {

        public static IDateBase CreateDateBase(BaseConfig config, Type entityType = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config), "数据库配置不能为空");

            // 处理关系型数据库配置
            if (config is DatabaseConfig dbConfig)
            {
                return CreateRelateConnection(dbConfig);
            }

            // 处理非关系型数据库配置
            if (config is NonRelationalDatabaseConfig nonRelationalConfig)
            {
                // 检查实体类型是否提供
                if (entityType == null)
                    throw new ArgumentNullException(nameof(entityType), "创建非关系型数据库连接时必须提供实体类型");

                // 使用反射创建泛型实例
                var method = typeof(DatabaseFactory)
                    .GetMethod("CreateNonRelateConnection", BindingFlags.Public | BindingFlags.Static)
                    .MakeGenericMethod(entityType);

                return (IDateBase)method.Invoke(null, new object[] { nonRelationalConfig });
            }

            throw new NotSupportedException($"不支持的配置类型: {config.GetType().Name}");
        }

        // 创建数据库连接的方法
        public static IRelationDateBase CreateRelateConnection(DatabaseConfig config)
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
