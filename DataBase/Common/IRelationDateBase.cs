using Server.DataBase.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

public interface IRelationDateBase : IDisposable, IDateBase
{
    // ========== 连接管理 ========== //
    Task OpenConnectionAsync(CancellationToken cancellationToken = default);
    Task CloseConnectionAsync(CancellationToken cancellationToken = default);

    // ========== 基础操作 ========== //
    Task<int> ExecuteNonQueryAsync(
        string sql,
        object? parameters = null,
        CommandType commandType = CommandType.Text,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default
    );

    Task<IEnumerable<T>> ExecuteQueryAsync<T>(
        string sql,
        object? parameters = null,
        CommandType commandType = CommandType.Text,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default
    ) where T : class, new();

    Task<T?> ExecuteScalarAsync<T>(
        string sql,
        object? parameters = null,
        CommandType commandType = CommandType.Text,
        int? commandTimeout = null,
        CancellationToken cancellationToken = default
    );

    // ========== 事务管理 ========== //
    Task BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        string? transactionName = null,
        CancellationToken cancellationToken = default
    );

    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(
        string? savepointName = null,
        CancellationToken cancellationToken = default
    );

    Task CreateSavepointAsync(
        string savepointName,
        CancellationToken cancellationToken = default
    );

    // ========== 状态与元数据 ========== //
    ConnectionState ConnectionState { get; }
    bool HasActiveTransaction { get; }
    string? ActiveTransactionName { get; }

    // ========== 高级功能 ========== //
    DbCommand CreateCommand(
        string commandText,
        CommandType commandType = CommandType.Text,
        int? commandTimeout = null
    );

    void RegisterParameterMapper<TParam>(Func<TParam, IEnumerable<DbParameter>> parameterMapper);
}