using Server.Common.Result;
using Server.Logger;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.DataBase.Implement.RelateSQL.MySQL
{
    public class LogDate : Connect
    {
        private ILogger _logger;

        public LogDate(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<Result> InsertLogAsync(string message)
        {
            var r = GetConnection();
            if (r.IsFailure)
            {
                if (r.HasException)
                {
                    return Result.Failure(r.ErrorMessage);
                }
                return Result.Failure(r.Errors);
            }

            IRelationDateBase dbConnection = r.Value;

            try
            {
                // 打开数据库连接
                await dbConnection.OpenConnectionAsync();

                // 执行查询操作，从 rotation_config 表读取数据
                var sql = "SELECT * FROM laser_config";
                var result = await dbConnection.ExecuteQueryAsync<LaserConfig>(sql);
                // 打印输出查询结果
                foreach (var item in result)
                {
                    Console.WriteLine($"ID: {item.Guid}, BaudRate: {item.BaudRate}, PortName: {item.PortName} {item.LaserType} {item.DoPort} {item.IP}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生异常: {ex.Message}");
            }
            finally
            {
                // 关闭数据库连接
                await dbConnection.CloseConnectionAsync();
            }
            return Result.Success();
        }
    }
}
