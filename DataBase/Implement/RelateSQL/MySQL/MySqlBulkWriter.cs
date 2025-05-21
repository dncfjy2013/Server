using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Server.DataBase.Core.RelateSQL;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace DataBase.Implement.RelateSQL.MySQL
{
    public class MySqlBulkWriter : IDisposable
    {
        private readonly MySqlDatabaseConnection _connection;
        private readonly HardwareInfo _hardwareInfo;
        private readonly int _batchSize;
        private readonly int _maxDegreeOfParallelism;
        private readonly bool _useTransaction;
        private readonly bool _optimizeConnection;
        private readonly bool _useBatchInsert;
        private readonly int _bulkCopyTimeout;
        private readonly bool _useTempTable;
        private readonly bool _skipDuplicates;
        private readonly bool _useBinaryProtocol;
        private readonly SemaphoreSlim _concurrencySemaphore;
        private readonly Dictionary<string, string> _mysqlConfigOverrides;
        private readonly bool _enableLogging;
        private readonly Action<string>? _logAction;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private long _totalRecordsWritten = 0;
        private readonly bool _useLoadData;
        private readonly string? _tempDirectory;

        public MySqlBulkWriter(
            MySqlDatabaseConnection connection,
            HardwareInfo? hardwareInfo = null,
            int? batchSize = null,
            int? maxDegreeOfParallelism = null,
            bool useTransaction = true,
            bool optimizeConnection = true,
            bool useBatchInsert = false,
            int bulkCopyTimeout = 300,
            bool useTempTable = false,
            bool skipDuplicates = false,
            bool useBinaryProtocol = true,
            Dictionary<string, string>? mysqlConfigOverrides = null,
            bool enableLogging = false,
            Action<string>? logAction = null,
            bool useLoadData = true,
            string? tempDirectory = null)
        {
            _connection = connection;
            if(hardwareInfo == null)
            {
                _hardwareInfo = HardwareDetector.DetectHardwareInfo();
            }
            _useTransaction = useTransaction;
            _optimizeConnection = optimizeConnection;
            _useBatchInsert = useBatchInsert;
            _bulkCopyTimeout = bulkCopyTimeout;
            _useTempTable = useTempTable;
            _skipDuplicates = skipDuplicates;
            _useBinaryProtocol = useBinaryProtocol;
            _mysqlConfigOverrides = mysqlConfigOverrides ?? new Dictionary<string, string>();
            _enableLogging = enableLogging;
            _logAction = logAction ?? Console.WriteLine;
            _useLoadData = useLoadData;
            _tempDirectory = tempDirectory;

            // 根据硬件条件自动优化参数
            (_batchSize, _maxDegreeOfParallelism) = OptimizeParameters(
                _hardwareInfo,
                batchSize,
                maxDegreeOfParallelism);

            _concurrencySemaphore = new SemaphoreSlim(_maxDegreeOfParallelism);

            Log($"初始化 - 硬件配置: CPU={_hardwareInfo.CpuInfo}, Cores={_hardwareInfo.CpuCores}, RAM={_hardwareInfo.TotalMemoryGb}GB, Disk={_hardwareInfo.DiskType}");
            Log($"初始化 - 优化参数: BatchSize={_batchSize}, MaxDegreeOfParallelism={_maxDegreeOfParallelism}, UseLoadData={_useLoadData}");
        }


        private (int BatchSize, int MaxDegreeOfParallelism) OptimizeParameters(
            HardwareInfo hardwareInfo,
            int? userBatchSize,
            int? userMaxDegreeOfParallelism)
        {
            // 基础参数
            int calculatedBatchSize = userBatchSize ?? CalculateOptimalBatchSize(hardwareInfo);
            int calculatedParallelism = userMaxDegreeOfParallelism ?? CalculateOptimalParallelism(hardwareInfo);

            // 应用用户覆盖配置
            if (_mysqlConfigOverrides.TryGetValue("batchSizeFactor", out var batchSizeFactorStr) &&
                double.TryParse(batchSizeFactorStr, out double batchSizeFactor))
            {
                calculatedBatchSize = (int)(calculatedBatchSize * batchSizeFactor);
            }

            if (_mysqlConfigOverrides.TryGetValue("parallelismFactor", out var parallelismFactorStr) &&
                double.TryParse(parallelismFactorStr, out double parallelismFactor))
            {
                calculatedParallelism = (int)(calculatedParallelism * parallelismFactor);
            }

            // 应用安全限制
            calculatedBatchSize = Math.Max(1000, Math.Min(calculatedBatchSize, 500000));
            calculatedParallelism = Math.Max(1, Math.Min(calculatedParallelism, Environment.ProcessorCount * 2));

            return (calculatedBatchSize, calculatedParallelism);
        }

        private int CalculateOptimalBatchSize(HardwareInfo hardwareInfo)
        {
            // 基础批次大小
            int baseBatchSize = 10000;

            // CPU核心数调整 (最多16核有效)
            double cpuFactor = Math.Min(hardwareInfo.CpuCores, 16) / 4.0;

            // 内存调整 (GB为单位)
            double memoryFactor = Math.Min(hardwareInfo.TotalMemoryGb / 16.0, 4.0);

            // 磁盘类型调整
            double diskFactor = hardwareInfo.DiskType switch
            {
                DiskType.NVMe => 3.0,
                DiskType.SSD => 2.0,
                _ => 1.0
            };

            // 最终批次大小
            return (int)(baseBatchSize * cpuFactor * memoryFactor * diskFactor);
        }

        private int CalculateOptimalParallelism(HardwareInfo hardwareInfo)
        {
            // 基础并行度
            int baseParallelism = 4;

            // CPU核心数调整 (最多32核有效)
            double cpuFactor = Math.Min(hardwareInfo.CpuCores, 32) / 4.0;

            // 内存调整 (GB为单位)
            double memoryFactor = Math.Min(hardwareInfo.TotalMemoryGb / 8.0, 4.0);

            // 磁盘类型调整
            double diskFactor = hardwareInfo.DiskType switch
            {
                DiskType.NVMe => 2.0,
                DiskType.SSD => 1.5,
                _ => 1.0
            };

            // 最终并行度
            return (int)(baseParallelism * cpuFactor * memoryFactor * diskFactor);
        }

        public async Task<WriteResult> WriteAsync<T>(
            string tableName,
            IEnumerable<T> entities,
            CancellationToken cancellationToken = default)
            where T : class, new()
        {
            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentNullException(nameof(tableName));

            if (entities == null)
                throw new ArgumentNullException(nameof(entities));

            _stopwatch.Restart();
            _totalRecordsWritten = 0;

            // 确保连接打开
            await _connection.OpenConnectionAsync(cancellationToken);

            // 应用MySQL优化配置
            if (_optimizeConnection)
            {
                await ApplyOptimizedMySqlConfig(cancellationToken);
            }

            try
            {
                // 开始事务
                if (_useTransaction)
                    await _connection.BeginTransactionAsync(cancellationToken: cancellationToken);

                if (_useTempTable)
                {
                    var tempTableName = $"temp_{tableName}_{Guid.NewGuid():N}";
                    await CreateTempTableAsync(tableName, tempTableName, cancellationToken);

                    try
                    {
                        // 写入临时表
                        await WriteToTempTableAsync(tempTableName, entities, cancellationToken);

                        // 从临时表导入到目标表
                        var insertSql = _skipDuplicates
                            ? $"INSERT IGNORE INTO `{tableName}` SELECT * FROM `{tempTableName}`"
                            : $"INSERT INTO `{tableName}` SELECT * FROM `{tempTableName}`";

                        await ExecuteNonQueryAsync(insertSql, cancellationToken: cancellationToken);

                        _totalRecordsWritten = await GetRecordCountAsync(tempTableName, cancellationToken);
                    }
                    finally
                    {
                        // 删除临时表
                        await ExecuteNonQueryAsync($"DROP TEMPORARY TABLE IF EXISTS `{tempTableName}`", cancellationToken: cancellationToken);
                    }
                }
                else
                {
                    // 直接写入目标表
                    await WriteDirectlyAsync(tableName, entities, cancellationToken);

                    // 获取写入的记录数
                    _totalRecordsWritten = await GetRecordCountAsync(tableName, cancellationToken);
                }

                // 提交事务
                if (_useTransaction)
                    await _connection.CommitTransactionAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // 回滚事务
                if (_useTransaction && _connection.HasActiveTransaction)
                    await _connection.RollbackTransactionAsync(cancellationToken: cancellationToken);

                Log($"写入过程中发生错误: {ex}");
                return new WriteResult
                {
                    Success = false,
                    RecordsWritten = 0,
                    ElapsedMilliseconds = _stopwatch.ElapsedMilliseconds,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                // 恢复连接参数
                if (_optimizeConnection)
                {
                    await ResetMySqlConfig(cancellationToken);
                }
            }

            _stopwatch.Stop();
            var result = new WriteResult
            {
                Success = true,
                RecordsWritten = _totalRecordsWritten,
                ElapsedMilliseconds = _stopwatch.ElapsedMilliseconds
            };

            Log($"写入完成 - 记录数: {result.RecordsWritten}, 耗时: {result.ElapsedMilliseconds}ms, 吞吐量: {result.ThroughputPerSecond:N0} 条/秒");

            return result;
        }

        private async Task ApplyOptimizedMySqlConfig(CancellationToken cancellationToken)
        {
            // 基础优化配置
            var configCommands = new List<string>
        {
            "SET autocommit=0;",
            "SET unique_checks=0;",
            "SET foreign_key_checks=0;",
            "SET sql_log_bin=0;", // 禁用二进制日志
        };

            // 根据硬件配置动态调整
            if (_hardwareInfo.TotalMemoryGb > 32)
            {
                configCommands.Add($"SET bulk_insert_buffer_size = {Math.Min((long)_hardwareInfo.TotalMemoryGb / 4 * 1024 * 1024 * 1024, 1024L * 1024 * 1024 * 8)};"); // 最多8GB
            }

            if (_hardwareInfo.DiskType == DiskType.SSD || _hardwareInfo.DiskType == DiskType.NVMe)
            {
                configCommands.Add("SET innodb_flush_log_at_trx_commit=2;"); // 提高写入性能
            }

            // 应用用户自定义配置
            foreach (var kvp in _mysqlConfigOverrides)
            {
                configCommands.Add($"SET {kvp.Key} = {kvp.Value};");
            }

            // 执行配置命令
            foreach (var command in configCommands)
            {
                await ExecuteNonQueryAsync(command, cancellationToken);
            }
        }

        private async Task ResetMySqlConfig(CancellationToken cancellationToken)
        {
            var resetCommands = new List<string>
        {
            "SET autocommit=1;",
            "SET unique_checks=1;",
            "SET foreign_key_checks=1;",
            "SET sql_log_bin=1;",
            "SET innodb_flush_log_at_trx_commit=1;"
        };

            foreach (var command in resetCommands)
            {
                await ExecuteNonQueryAsync(command, cancellationToken);
            }
        }

        private async Task WriteDirectlyAsync<T>(string tableName, IEnumerable<T> entities, CancellationToken cancellationToken)
            where T : class, new()
        {
            var batches = entities.Chunk(_batchSize);
            var tasks = new List<Task>();
            long totalBatches = 0;
            long completedBatches = 0;

            // 预计算批次总数用于进度跟踪
            if (entities is ICollection<T> collection)
            {
                totalBatches = (long)Math.Ceiling(collection.Count / (double)_batchSize);
            }

            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _concurrencySemaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (_useBatchInsert)
                        {
                            await WriteBatchUsingMultiInsertAsync(tableName, batch, cancellationToken);
                        }
                        else
                        {
                            await WriteBatchUsingBulkCopyAsync(tableName, batch, cancellationToken);
                        }

                        Interlocked.Increment(ref completedBatches);

                        if (_enableLogging && totalBatches > 0 && completedBatches % 10 == 0)
                        {
                            var progress = (int)((completedBatches / (double)totalBatches) * 100);
                            Log($"进度: {progress}% ({completedBatches}/{totalBatches} 批次)");
                        }
                    }
                    finally
                    {
                        _concurrencySemaphore.Release();
                    }
                }, cancellationToken));

                // 控制并发任务数量，避免内存溢出
                if (tasks.Count >= _maxDegreeOfParallelism * 2)
                {
                    await Task.WhenAny(tasks);
                    tasks = tasks.Where(t => !t.IsCompleted).ToList();
                }
            }

            await Task.WhenAll(tasks);
        }

        private async Task WriteToTempTableAsync<T>(string tempTableName, IEnumerable<T> entities, CancellationToken cancellationToken)
            where T : class, new()
        {
            var batches = entities.Chunk(_batchSize);

            foreach (var batch in batches)
            {
                if (_useBatchInsert)
                {
                    await WriteBatchUsingMultiInsertAsync(tempTableName, batch, cancellationToken);
                }
                else
                {
                    await WriteBatchUsingBulkCopyAsync(tempTableName, batch, cancellationToken);
                }
            }
        }

        private async Task CreateTempTableAsync(string sourceTableName, string tempTableName, CancellationToken cancellationToken)
        {
            // 创建临时表
            await ExecuteNonQueryAsync($"CREATE TEMPORARY TABLE `{tempTableName}` LIKE `{sourceTableName}`", cancellationToken);

            // 禁用临时表的索引以提高写入性能
            await ExecuteNonQueryAsync($"ALTER TABLE `{tempTableName}` DISABLE KEYS", cancellationToken);
        }

        private async Task WriteBatchUsingBulkCopyAsync<T>(string tableName, T[] batch, CancellationToken cancellationToken)
            where T : class, new()
        {
            if (batch.Length == 0)
                return;

            if (_useLoadData)
            {
                await WriteBatchUsingLoadDataAsync(tableName, batch, cancellationToken);
            }
            else
            {
                await WriteBatchUsingMultiInsertAsync(tableName, batch, cancellationToken);
            }
        }

        private async Task WriteBatchUsingLoadDataAsync<T>(string tableName, T[] batch, CancellationToken cancellationToken)
            where T : class, new()
        {
            if (batch.Length == 0)
                return;

            var tempFile = GetTempFilePath();
            try
            {
                // 将数据写入临时文件
                await WriteEntitiesToTempFileAsync(batch, tempFile, cancellationToken);

                // 使用LOAD DATA INFILE导入数据
                var sql = $"LOAD DATA LOCAL INFILE '{EscapePath(tempFile)}' " +
                          $"INTO TABLE `{tableName}` " +
                          $"FIELDS TERMINATED BY '\t' " +
                          $"ENCLOSED BY '\"' " +
                          $"ESCAPED BY '\\\\' " +
                          $"LINES TERMINATED BY '\n'";

                await ExecuteNonQueryAsync(sql, cancellationToken);
            }
            finally
            {
                // 删除临时文件
                try { File.Delete(tempFile); } catch { /* 忽略删除错误 */ }
            }
        }

        private string GetTempFilePath()
        {
            var directory = _tempDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"bulk_insert_{Guid.NewGuid()}.tmp");
        }

        private string EscapePath(string path)
        {
            return path.Replace("\\", "\\\\").Replace("'", "\\'");
        }

        private async Task WriteEntitiesToTempFileAsync<T>(T[] entities, string filePath, CancellationToken cancellationToken)
            where T : class, new()
        {
            var properties = typeof(T).GetProperties()
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToList();

            using var stream = File.Create(filePath);
            using var writer = new StreamWriter(stream, Encoding.UTF8);

            foreach (var entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var values = new List<string>();
                foreach (var property in properties)
                {
                    var value = property.GetValue(entity);
                    values.Add(EscapeCsvValue(value));
                }

                await writer.WriteLineAsync(string.Join("\t", values));
            }
        }

        private string EscapeCsvValue(object? value)
        {
            if (value == null || value is DBNull)
                return string.Empty;

            if (value is string str)
            {
                return $"\"{str.Replace("\"", "\"\"")}\"";
            }

            if (value is DateTime dt)
            {
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            }

            if (value is DateTimeOffset dto)
            {
                return dto.ToString("yyyy-MM-dd HH:mm:ss");
            }

            return value.ToString() ?? string.Empty;
        }

        private async Task WriteBatchUsingMultiInsertAsync<T>(string tableName, T[] batch, CancellationToken cancellationToken)
            where T : class, new()
        {
            if (batch.Length == 0)
                return;

            var properties = typeof(T).GetProperties()
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToList();

            if (!properties.Any())
                return;

            var columnNames = string.Join(", ", properties.Select(p => $"`{p.Name}`"));

            // 使用StringBuilder构建SQL
            using var pooledStringBuilder = new PooledStringBuilder();
            var sb = pooledStringBuilder.Builder;

            sb.Append($"INSERT {(_skipDuplicates ? "IGNORE " : "")}INTO `{tableName}` ({columnNames}) VALUES ");

            for (int i = 0; i < batch.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("(");

                var item = batch[i];
                for (int j = 0; j < properties.Count; j++)
                {
                    if (j > 0) sb.Append(", ");

                    var value = properties[j].GetValue(item);
                    AppendSqlValue(sb, value);
                }

                sb.Append(")");
            }

            var sql = sb.ToString();
            await _connection.ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken);
        }

        private void AppendSqlValue(StringBuilder sb, object? value)
        {
            if (value == null || value is DBNull)
            {
                sb.Append("NULL");
                return;
            }

            switch (value)
            {
                case string str:
                    sb.Append("'").Append(str.Replace("'", "''")).Append("'");
                    break;
                case DateTime dt:
                    sb.Append("'").Append(dt.ToString("yyyy-MM-dd HH:mm:ss")).Append("'");
                    break;
                case DateTimeOffset dto:
                    sb.Append("'").Append(dto.ToString("yyyy-MM-dd HH:mm:ss")).Append("'");
                    break;
                case bool b:
                    sb.Append(b ? "1" : "0");
                    break;
                case byte[] bytes:
                    sb.Append("0x").Append(BitConverter.ToString(bytes).Replace("-", ""));
                    break;
                default:
                    sb.Append(value.ToString());
                    break;
            }
        }

        private DataTable ConvertToDataTable<T>(T[] entities) where T : class, new()
        {
            var dataTable = new DataTable();
            var properties = typeof(T).GetProperties()
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToList();

            foreach (var property in properties)
            {
                dataTable.Columns.Add(property.Name, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
            }

            var rows = ArrayPool<DataRow>.Shared.Rent(entities.Length);
            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var row = dataTable.NewRow();
                    rows[i] = row;

                    var entity = entities[i];
                    foreach (var property in properties)
                    {
                        row[property.Name] = property.GetValue(entity) ?? DBNull.Value;
                    }
                }

                dataTable.BeginLoadData();
                foreach (var row in rows.Take(entities.Length))
                {
                    dataTable.Rows.Add(row);
                }
                dataTable.EndLoadData();
            }
            finally
            {
                ArrayPool<DataRow>.Shared.Return(rows);
            }

            return dataTable;
        }

        private async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken = default)
        {
            await _connection.ExecuteNonQueryAsync(sql, cancellationToken: cancellationToken);
        }

        private async Task<long> GetRecordCountAsync(string tableName, CancellationToken cancellationToken)
        {
            var sql = $"SELECT COUNT(*) FROM `{tableName}`";
            return await _connection.ExecuteScalarAsync<long>(sql, cancellationToken: cancellationToken);
        }

        private void Log(string message)
        {
            if (_enableLogging)
            {
                _logAction?.Invoke($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
            }
        }

        public void Dispose()
        {
            _concurrencySemaphore.Dispose();

            if (_connection.HasActiveTransaction)
            {
                _connection.RollbackTransactionAsync().Wait();
            }
        }
    }

    public class WriteResult
    {
        public bool Success { get; set; }
        public long RecordsWritten { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public string? ErrorMessage { get; set; }

        public double ThroughputPerSecond =>
            ElapsedMilliseconds > 0
                ? RecordsWritten / (ElapsedMilliseconds / 1000.0)
                : 0;
    }

    internal sealed class PooledStringBuilder : IDisposable
    {
        private readonly StringBuilder _builder;
        private readonly ObjectPool<StringBuilder> _pool;

        public StringBuilder Builder => _builder;

        public PooledStringBuilder(ObjectPool<StringBuilder>? pool = null)
        {
            _pool = pool ?? DefaultPool;
            _builder = _pool.Get();
        }

        public void Dispose()
        {
            _builder.Clear();
            _pool.Return(_builder);
        }

        public override string ToString()
        {
            return _builder.ToString();
        }

        private static readonly ObjectPool<StringBuilder> DefaultPool =
            new ObjectPool<StringBuilder>(() => new StringBuilder(1024 * 10), 100);
    }

    internal sealed class ObjectPool<T> where T : class
    {
        private readonly Func<T> _createFunc;
        private readonly ConcurrentBag<T> _objects;

        public ObjectPool(Func<T> createFunc, int initialCapacity)
        {
            _createFunc = createFunc;
            _objects = new ConcurrentBag<T>();

            for (int i = 0; i < initialCapacity; i++)
            {
                _objects.Add(createFunc());
            }
        }

        public T Get() => _objects.TryTake(out var item) ? item : _createFunc();

        public void Return(T item) => _objects.Add(item);
    }
}
