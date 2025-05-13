using MySql.Data.MySqlClient;
using Server.DataBase;
using Server.DataBase.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Test
{
    public class LaserConfig
    {
        public string Guid { get; set; }
        /// <summary>
        /// 串口端口名称
        /// </summary>
        public string PortName { get; set; } = string.Empty;
        /// <summary>
        /// 波特率
        /// </summary>
        public int BaudRate { get; set; }
        /// <summary>
        /// 激光器类型
        /// </summary>
        public string LaserType { get; set; } = string.Empty;
        /// <summary>
        /// 数字输出端口
        /// </summary>
        public string DoPort { get; set; } = string.Empty;
        /// <summary>
        /// IP地址
        /// </summary>
        public string IP { get; set; } = string.Empty;
        /// <summary>
        /// 端口
        /// </summary>
        public string Port { get; set; }
    }
    public class DateTest
    {

        public async Task MysqlTestAsync()
        {
            // 配置数据库连接信息
            BaseConfig config = new DatabaseConfig
            {
                DatabaseType = DatabaseType.MySql,
                Host = "192.168.112.200",
                Port = 3306,
                DatabaseName = "fsd_2501",
                UserId = "fsd",
                Password = "yzqjmyq@123",
            };

            // 创建数据库连接对象
            IRelationDateBase dbConnection = (IRelationDateBase)DatabaseFactory.CreateDateBase(config);

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
        }
    }
}
