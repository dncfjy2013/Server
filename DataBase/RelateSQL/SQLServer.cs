using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace Server.DataBase.RelateSQL
{
    public class SqlDatabaseConnection : IRelationDateBase
    {
        private readonly SqlConnection _connection;
        private readonly AsyncLocal<SqlTransaction> _transaction = new AsyncLocal<SqlTransaction>();
        private readonly DatabaseConfig _config;
        private readonly Dictionary<Type, Func<object, IEnumerable<DbParameter>>> _parameterMappers = new Dictionary<Type, Func<object, IEnumerable<DbParameter>>>();

        public SqlDatabaseConnection(DatabaseConfig config)
        {
            _config = config;
            _connection = new SqlConnection(_config.ConnectionString);
            // 开启连接池
            _connection.ConnectionString = _config.ConnectionString + ";Pooling=true;Max Pool Size=100;";
        }

        public ConnectionState ConnectionState => _connection.State;
        public bool HasActiveTransaction => _transaction.Value != null;
        public string? ActiveTransactionName => null; // 因为 SqlTransaction 没有 TransactionName 属性

        public async Task OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_connection.State != ConnectionState.Open)
            {
                await _connection.OpenAsync(cancellationToken);
            }
        }

        public async Task CloseConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_connection.State != ConnectionState.Closed)
            {
                await _connection.CloseAsync();
            }
        }

        public async Task<int> ExecuteNonQueryAsync(
            string sql,
            object? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    using var command = CreateCommand(sql, commandType, commandTimeout);
                    AddParameters(command, parameters);
                    return await command.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException ex) when (IsTransientError(ex))
                {
                    retries--;
                    if (retries > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return 0;
        }

        public async Task<IEnumerable<T>> ExecuteQueryAsync<T>(
            string sql,
            object? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeout = null,
            CancellationToken cancellationToken = default
        ) where T : class, new()
        {
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    using var command = CreateCommand(sql, commandType, commandTimeout);
                    AddParameters(command, parameters);
                    using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    var result = new List<T>();
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var item = new T();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var property = typeof(T).GetProperty(reader.GetName(i));
                            if (property != null && !reader.IsDBNull(i))
                            {
                                property.SetValue(item, reader.GetValue(i));
                            }
                        }
                        result.Add(item);
                    }
                    return result;
                }
                catch (SqlException ex) when (IsTransientError(ex))
                {
                    retries--;
                    if (retries > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return new List<T>();
        }

        public async Task<T?> ExecuteScalarAsync<T>(
            string sql,
            object? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            int retries = 3;
            while (retries > 0)
            {
                try
                {
                    using var command = CreateCommand(sql, commandType, commandTimeout);
                    AddParameters(command, parameters);
                    var result = await command.ExecuteScalarAsync(cancellationToken);
                    return result == DBNull.Value ? default : (T)result;
                }
                catch (SqlException ex) when (IsTransientError(ex))
                {
                    retries--;
                    if (retries > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return default;
        }

        public async Task BeginTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            string? transactionName = null,
            CancellationToken cancellationToken = default
        )
        {
            await OpenConnectionAsync(cancellationToken);
            _transaction.Value = (SqlTransaction?)await _connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        }

        public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction.Value != null)
            {
                try
                {
                    await _transaction.Value.CommitAsync(cancellationToken);
                }
                finally
                {
                    _transaction.Value.Dispose();
                    _transaction.Value = null;
                }
            }
        }

        public async Task RollbackTransactionAsync(
            string? savepointName = null,
            CancellationToken cancellationToken = default
        )
        {
            if (_transaction.Value != null)
            {
                try
                {
                    if (string.IsNullOrEmpty(savepointName))
                    {
                        await _transaction.Value.RollbackAsync(cancellationToken);
                    }
                    else
                    {
                        // 注意：SqlTransaction 没有直接的 RollbackAsync 带 savepointName 的重载，这里只是示例结构
                        throw new NotImplementedException("Savepoint rollback is not fully implemented.");
                    }
                }
                finally
                {
                    _transaction.Value.Dispose();
                    _transaction.Value = null;
                }
            }
        }

        public async Task CreateSavepointAsync(
            string savepointName,
            CancellationToken cancellationToken = default
        )
        {
            if (_transaction.Value != null)
            {
                // 注意：SqlTransaction 没有直接的 SaveAsync 方法，这里只是示例结构
                throw new NotImplementedException("Savepoint creation is not fully implemented.");
            }
        }

        public DbCommand CreateCommand(
            string commandText,
            CommandType commandType = CommandType.Text,
            int? commandTimeout = null
        )
        {
            var command = _connection.CreateCommand();
            command.CommandText = commandText;
            command.CommandType = commandType;
            if (commandTimeout.HasValue)
            {
                command.CommandTimeout = commandTimeout.Value;
            }
            if (_transaction.Value != null)
            {
                command.Transaction = _transaction.Value;
            }
            return command;
        }

        public void RegisterParameterMapper<TParam>(Func<TParam, IEnumerable<DbParameter>> parameterMapper)
        {
            _parameterMappers[typeof(TParam)] = obj => parameterMapper((TParam)obj);
        }

        private void AddParameters(DbCommand command, object? parameters)
        {
            if (parameters == null) return;

            if (_parameterMappers.TryGetValue(parameters.GetType(), out var mapper))
            {
                foreach (var param in mapper(parameters))
                {
                    command.Parameters.Add(param);
                }
            }
            else
            {
                var properties = parameters.GetType().GetProperties();
                foreach (var property in properties)
                {
                    var param = command.CreateParameter();
                    param.ParameterName = $"@{property.Name}";
                    param.Value = property.GetValue(parameters) ?? DBNull.Value;
                    command.Parameters.Add(param);
                }
            }
        }

        public void Dispose()
        {
            _transaction.Value?.Dispose();
            _connection.Dispose();
            GC.SuppressFinalize(this);
        }

        private bool IsTransientError(SqlException ex)
        {
            // 检查是否为可重试的错误
            switch (ex.Number)
            {
                case -2: // 超时
                case 233: // 客户端与服务器连接中断
                case 10053: // 客户端与服务器连接被重置
                case 10054: // 客户端与服务器连接被强制关闭
                case 10060: // 客户端无法连接到服务器
                    return true;
                default:
                    return false;
            }
        }
    }
}
