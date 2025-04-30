using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Server.DataBase.RelateSQL;
using System;

namespace Server.DataBase
{
    public class DatabaseFactory
    {
        // 创建数据库连接的方法
        public static IDatabaseConnection CreateConnection(DatabaseConfig config)
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
    }
}
