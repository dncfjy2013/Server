using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Npgsql;
using System.Threading;
using System.Threading.Tasks;

namespace Server.DataBase.Core.RelateSQL
{
    public class PostgreSQLDatabaseConnection : IRelationDateBase
    {
        private readonly NpgsqlConnection _connection;
        private readonly AsyncLocal<NpgsqlTransaction> _transaction = new AsyncLocal<NpgsqlTransaction>();
        private readonly DatabaseConfig _config;
        private readonly Dictionary<Type, Func<object, IEnumerable<DbParameter>>> _parameterMappers = new Dictionary<Type, Func<object, IEnumerable<DbParameter>>>();

        public PostgreSQLDatabaseConnection(DatabaseConfig config)
        {
            _config = config;
            // 配置连接池参数
            var connectionStringBuilder = new NpgsqlConnectionStringBuilder(_config.ConnectionString)
            {
                Pooling = true,
                MaxPoolSize = 100, // 最大连接数
                MinPoolSize = 10,  // 最小连接数
            };
            _connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
        }

        public ConnectionState ConnectionState => _connection.State;
        public bool HasActiveTransaction => _transaction.Value != null;
        public string? ActiveTransactionName => null;

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
                catch (NpgsqlException ex) when (IsTransientError(ex))
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
                catch (NpgsqlException ex) when (IsTransientError(ex))
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
                    return result == DBNull.Value ? default : (T)Convert.ChangeType(result, typeof(T));
                }
                catch (NpgsqlException ex) when (IsTransientError(ex))
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
            _transaction.Value = await _connection.BeginTransactionAsync(isolationLevel, cancellationToken);
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
                        await _transaction.Value.RollbackAsync(savepointName, cancellationToken);
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
                using var command = _connection.CreateCommand();
                command.Transaction = _transaction.Value;
                command.CommandText = $"SAVEPOINT {savepointName}";
                await command.ExecuteNonQueryAsync(cancellationToken);
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

        private bool IsTransientError(NpgsqlException ex)
        {
            // 这里可以根据 PostgreSQL 的错误码来判断是否是暂时性错误
            // 例如，某些网络错误、数据库连接超时等
            // 以下是一些示例错误码，具体请根据实际情况调整
            switch (ex.SqlState)
            {
                case "08001": // 连接拒绝
                case "08006": // 连接失败
                case "08P01": // 取消连接
                    return true;
                default:
                    return false;
            }
        }
    }
}