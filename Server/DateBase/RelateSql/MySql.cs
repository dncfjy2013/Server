using DataBase.Implement.RelateSQL.MySQL;
using MySql.Data.MySqlClient;
using Server.DataBase.Core;
using Server.DataBase.Core.RelateSQL;

namespace Server.DateBase.RelateSql
{
    // 产品实体类
    public class Product
    {
        public int Id { get; set; }         // 主键（自增）
        public string Name { get; set; }    // 名称
        public decimal Price { get; set; }  // 价格
        public string Description { get; set; } // 描述
        public DateTime CreatedTime { get; set; } = DateTime.Now; // 创建时间
    }
    public static class MySqlHelper
    {
        // 获取 MySQL 连接（单例或池化连接）
        public static MySqlDatabaseConnection GetMySqlConnection(DatabaseConfig config)
        {
            
            var connection = DatabaseFactory.CreateRelateConnection(config);
            connection.OpenConnectionAsync().GetAwaiter().GetResult(); // 同步打开连接（示例简化，实际建议用异步）
            return (MySqlDatabaseConnection)connection;
        }

        // 基础插入（单条）
        public static async Task<int> InsertProductAsync(MySqlDatabaseConnection connection, Product product)
        {
            var sql = @"
            INSERT INTO Products (Name, Price, Description, CreatedTime)
            VALUES (@Name, @Price, @Description, @CreatedTime);
            SELECT LAST_INSERT_ID();"; // 返回自增ID

            return await connection.ExecuteScalarAsync<int>(sql, new MySqlParameter[] {
            new MySqlParameter("@Name", product.Name),
            new MySqlParameter("@Price", product.Price),
            new MySqlParameter("@Description", product.Description),
            new MySqlParameter("@CreatedTime", product.CreatedTime)
        });
        }

        // 批量插入（使用 MySqlBulkWriter）
        public static async Task<WriteResult> BulkInsertProductsAsync(
            MySqlDatabaseConnection connection,
            IEnumerable<Product> products,
            bool useLoadData = true)
        {
            var bulkWriter = new MySqlBulkWriter(
                connection,
                useLoadData: useLoadData,       // 启用高性能 LOAD DATA 模式
                batchSize: 10000,              // 每批1万条（自动优化）
                maxDegreeOfParallelism: Environment.ProcessorCount, // 自动适配CPU核心数
                enableLogging: true,            // 启用日志输出
                tempDirectory: @"D:\Temp"       // 临时文件目录（需提前创建）
            );

            return await bulkWriter.WriteAsync<Product>("Products", products);
        }

        // 事务操作示例
        public static async Task<(bool Success, int AffectedRows)> TransactionExampleAsync(
            MySqlDatabaseConnection connection,
            Product product1,
            Product product2)
        {
            try
            {
                await connection.BeginTransactionAsync(); // 开启事务

                // 执行第一条插入
                await InsertProductAsync(connection, product1);

                // 模拟错误（可取消注释触发回滚）
                // throw new InvalidOperationException("模拟事务失败");

                // 执行第二条插入
                var affectedRows = await InsertProductAsync(connection, product2);

                await connection.CommitTransactionAsync(); // 提交事务
                return (true, affectedRows);
            }
            catch (Exception ex)
            {
                await connection.RollbackTransactionAsync(); // 回滚事务
                Console.WriteLine($"事务失败：{ex.Message}");
                return (false, 0);
            }
        }
    }
    class test
    {
        static async Task TestMain()
        {
            // 配置 MySQL 连接参数
            var mysqlConfig = new DatabaseConfig
            {
                DatabaseType = DatabaseType.MySql,
                Host = "localhost",
                Port = 3306,
                DatabaseName = "test_db",
                UserId = "root",
                Password = "your_password",
                // 其他配置（如连接超时、SSL等）
                ConnectionTimeout = 30,
                Encrypt = false,
                TrustServerCertificate = true
            };

            // 获取 MySQL 连接
            using var connection = MySqlHelper.GetMySqlConnection(mysqlConfig);

            // 示例 1：单条插入
            var singleProduct = new Product
            {
                Name = "Test Product",
                Price = 99.99m,
                Description = "Sample description"
            };
            var insertedId = await MySqlHelper.InsertProductAsync(connection, singleProduct);
            Console.WriteLine($"单条插入成功，ID：{insertedId}");

            // 示例 2：生成测试数据（10万条）
            var testProducts = Enumerable.Range(1, 100000).Select(i => new Product
            {
                Name = $"Product-{i}",
                Price = i * 0.99m,
                Description = $"Description for Product {i}"
            }).ToList();

            // 示例 3：高性能批量插入（使用 LOAD DATA）
            var bulkResult = await MySqlHelper.BulkInsertProductsAsync(connection, testProducts);
            if (bulkResult.Success)
            {
                Console.WriteLine($"批量插入完成：{bulkResult.RecordsWritten}条，耗时{bulkResult.ElapsedMilliseconds}ms");
                Console.WriteLine($"吞吐量：{bulkResult.ThroughputPerSecond:N0} 条/秒");
            }

            // 示例 4：事务操作
            var productA = new Product { Name = "A", Price = 10 };
            var productB = new Product { Name = "B", Price = 20 };
            var transResult = await MySqlHelper.TransactionExampleAsync(connection, productA, productB);
            Console.WriteLine($"事务操作：{(transResult.Success ? "成功" : "失败")}，影响行数：{transResult.AffectedRows}");

            // 示例 5：简单查询（验证数据）
            var queryResult = await connection.ExecuteQueryAsync<Product>(
                "SELECT TOP 10 * FROM Products ORDER BY CreatedTime DESC",
                null); // 无参数
            Console.WriteLine($"查询结果：{queryResult.ToList().Count} 条数据");
        }
    }
}
