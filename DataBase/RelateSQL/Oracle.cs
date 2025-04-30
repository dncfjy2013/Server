using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.DataBase.RelateSQL
{
    public class OracleDatabaseConnection : IDatabaseConnection
    {
        private readonly OracleConnection _connection;
        private OracleTransaction? _transaction;
        private readonly DatabaseConfig _config;
        private readonly Dictionary<Type, Func<object, IEnumerable<DbParameter>>> _parameterMappers = new Dictionary<Type, Func<object, IEnumerable<DbParameter>>>();

        public OracleDatabaseConnection(DatabaseConfig config)
        {
            _config = config;
            _connection = new OracleConnection(_config.ConnectionString);
        }

        public ConnectionState ConnectionState => _connection.State;
        public bool HasActiveTransaction => _transaction != null;
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
            using var command = CreateCommand(sql, commandType, commandTimeout);
            AddParameters(command, parameters);
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<IEnumerable<T>> ExecuteQueryAsync<T>(
            string sql,
            object? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeout = null,
            CancellationToken cancellationToken = default
        ) where T : class, new()
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

        public async Task<T?> ExecuteScalarAsync<T>(
            string sql,
            object? parameters = null,
            CommandType commandType = CommandType.Text,
            int? commandTimeout = null,
            CancellationToken cancellationToken = default
        )
        {
            using var command = CreateCommand(sql, commandType, commandTimeout);
            AddParameters(command, parameters);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result == DBNull.Value ? default : (T)Convert.ChangeType(result, typeof(T));
        }

        public async Task BeginTransactionAsync(
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
            string? transactionName = null,
            CancellationToken cancellationToken = default
        )
        {
            await OpenConnectionAsync(cancellationToken);
            _transaction = (OracleTransaction?)await _connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        }

        public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (_transaction != null)
            {
                await _transaction.CommitAsync(cancellationToken);
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync(
            string? savepointName = null,
            CancellationToken cancellationToken = default
        )
        {
            if (_transaction != null)
            {
                if (string.IsNullOrEmpty(savepointName))
                {
                    await _transaction.RollbackAsync(cancellationToken);
                }
                else
                {
                    // OracleTransaction 没有直接的 RollbackAsync 带 savepointName 的重载，这里只是示例结构
                    throw new NotImplementedException("Savepoint rollback is not fully implemented.");
                }
                _transaction.Dispose();
                _transaction = null;
            }
        }

        public async Task CreateSavepointAsync(
            string savepointName,
            CancellationToken cancellationToken = default
        )
        {
            if (_transaction != null)
            {
                // OracleTransaction 没有直接的 SaveAsync 方法，这里通过执行 SQL 语句来模拟创建保存点
                using var command = _connection.CreateCommand();
                command.Transaction = _transaction;
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
            if (_transaction != null)
            {
                command.Transaction = _transaction;
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
            _transaction?.Dispose();
            _connection.Dispose();
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
            }
            await _connection.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
