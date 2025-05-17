using Server.Common.Result;
using Server.DataBase.Common;
using Server.DataBase.Core;
using System;

namespace Server.DataBase.Implement.RelateSQL.MySQL
{
    public class Connect
    {
        // 配置数据库连接信息
        private BaseConfig _defaultConfig = new DatabaseConfig
        {
            DatabaseType = DatabaseType.MySql,
            Host = "192.168.112.200",
            Port = 3306,
            DatabaseName = "fsd_2501",
            UserId = "fsd",
            Password = "yzqjmyq@123"
        };

        public Result<IRelationDateBase> GetConnection(BaseConfig config = null)
        {
            try
            {
                var effectiveConfig = config ?? _defaultConfig;

                if (effectiveConfig is not DatabaseConfig mysqlConfig ||
                    mysqlConfig.DatabaseType != DatabaseType.MySql)
                {
                    return (Result<IRelationDateBase>)Result.Failure("The configured database type is not MySQL, unable to create the connection.");
                }

                var connection = DatabaseFactory.CreateDateBase(effectiveConfig) as IRelationDateBase;

                if (connection == null)
                {
                    return (Result<IRelationDateBase>)Result.Failure("The created database connection object is null, unable to obtain a valid connection.");
                }

                return Result.Success(connection);
            }
            catch (Exception ex)
            {
                return (Result<IRelationDateBase>)Result.Failure(ex);
            }
        }
    }
}