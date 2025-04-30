using Server.DataBase.NonRelateSQL.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Server.DataBase.NonRelateSQL.Common
{
    /// <summary>
    /// 非关系型通用数据集接口，定义了对数据集的基本操作和高级功能。
    /// </summary>
    /// <typeparam name="T">数据集中元素的类型。</typeparam>
    public interface INonRelationalDataset<T> : IAsyncDisposable
    {
        // ========== 核心CRUD操作 ========== //
        /// <summary>
        /// 向数据集中插入单个实体，并返回插入实体的唯一标识。
        /// </summary>
        /// <param name="entity">要插入的实体。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>插入实体的唯一标识。</returns>
        Task<string> InsertAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 批量向数据集中插入多个实体，并返回插入实体的唯一标识列表。
        /// </summary>
        /// <param name="entities">要插入的实体集合。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>插入实体的唯一标识列表。</returns>
        Task<IReadOnlyList<string>> BulkInsertAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据唯一标识从数据集中获取单个实体。
        /// </summary>
        /// <param name="id">实体的唯一标识。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>找到的实体，如果不存在则返回 null。</returns>
        Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// 更新数据集中的单个实体。
        /// </summary>
        /// <param name="entity">要更新的实体。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>如果更新成功返回 true，否则返回 false。</returns>
        Task<bool> UpdateAsync(T entity, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据唯一标识从数据集中删除单个实体。
        /// </summary>
        /// <param name="id">实体的唯一标识。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>如果删除成功返回 true，否则返回 false。</returns>
        Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

        // ========== 高级查询 ========== //
        /// <summary>
        /// 根据查询选项从数据集中异步获取所有符合条件的实体。
        /// </summary>
        /// <param name="options">查询选项，包括筛选条件、排序选项和限制数量等。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>符合条件的实体的异步可枚举集合。</returns>
        IAsyncEnumerable<T> FindAllAsync(QueryOptions<T>? options = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据分页选项和查询选项对数据集进行分页查询。
        /// </summary>
        /// <param name="pagination">分页选项，包括页码、每页大小和最大每页大小等。</param>
        /// <param name="query">查询选项，包括筛选条件、排序选项和限制数量等。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>分页查询结果，包括当前页的实体集合、页码、每页大小和总记录数等信息。</returns>
        Task<PaginatedResult<T>> PaginateAsync(PaginationOptions pagination, QueryOptions<T>? query = null, CancellationToken cancellationToken = default);

        // ========== 聚合操作 ========== //
        /// <summary>
        /// 计算数据集中符合指定筛选条件的实体数量。
        /// </summary>
        /// <param name="filter">筛选条件表达式。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>符合条件的实体数量。</returns>
        Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 判断数据集中是否存在符合指定筛选条件的实体。
        /// </summary>
        /// <param name="filter">筛选条件表达式。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>如果存在符合条件的实体返回 true，否则返回 false。</returns>
        Task<bool> ExistsAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default);

        // ========== 索引管理 ========== //
        /// <summary>
        /// 在数据集上创建指定名称和定义的索引。
        /// </summary>
        /// <param name="indexName">索引的名称。</param>
        /// <param name="definition">索引的定义，包括索引名称、键表达式、是否唯一和索引类型等。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>表示操作完成的任务。</returns>
        Task CreateIndexAsync(string indexName, IndexDefinition<T> definition, CancellationToken cancellationToken = default);

        /// <summary>
        /// 删除数据集中指定名称的索引。
        /// </summary>
        /// <param name="indexName">要删除的索引的名称。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>表示操作完成的任务。</returns>
        Task DropIndexAsync(string indexName, CancellationToken cancellationToken = default);
    }

    // ========== 支持类型 ========== //
    /// <summary>
    /// 分页查询结果记录类型，包含当前页的实体集合、页码、每页大小和总记录数等信息。
    /// </summary>
    /// <typeparam name="T">数据集中元素的类型。</typeparam>
    public record PaginatedResult<T>(
        IEnumerable<T> Items,
        int PageNumber,
        int PageSize,
        long TotalCount)
    {
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    }

    /// <summary>
    /// 查询选项类，用于指定筛选条件、排序选项和限制数量等查询参数。
    /// </summary>
    /// <typeparam name="T">数据集中元素的类型。</typeparam>
    public class QueryOptions<T>
    {
        public Expression<Func<T, bool>>? Filter { get; set; }
        public SortOptions<T>? Sorting { get; set; }
        public int? Limit { get; set; }
    }

    /// <summary>
    /// 排序选项类，用于指定排序的键选择器和排序方向。
    /// </summary>
    /// <typeparam name="T">数据集中元素的类型。</typeparam>
    public class SortOptions<T>
    {
        public Expression<Func<T, object>> KeySelector { get; set; } = null!;
        public bool IsAscending { get; set; } = true;
    }

}
/// <summary>
/// 分页选项类，用于指定页码、每页大小和最大每页大小等分页参数。
/// </summary>
public class PaginationOptions
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int MaxPageSize { get; set; } = 100;
    }

    /// <summary>
    /// 索引定义类，用于指定索引的名称、键表达式、是否唯一和索引类型等信息。
    /// </summary>
    /// <typeparam name="T">数据集中元素的类型。</typeparam>
    public class IndexDefinition<T>
    {
        public string Name { get; set; } = null!;
        public Expression<Func<T, object>> KeyExpression { get; set; } = null!;
        public bool IsUnique { get; set; }
        public IndexType Type { get; set; } = IndexType.BTree;
    }

    /// <summary>
    /// 索引类型枚举，定义了支持的索引类型。
    /// BTree: B树索引，适用于范围查询和排序操作。
    /// Hash: 哈希索引，适用于等值查询。
    /// FullText: 全文索引，适用于文本搜索。
    /// Spatial: 空间索引，适用于地理空间数据的查询。
    /// </summary>
    public enum IndexType
    {
        BTree,
        Hash,
        FullText,
        Spatial
    }

    // ========== 事务接口 ========== //
    /// <summary>
    /// 数据集事务接口，定义了事务的提交、回滚操作和获取事务唯一标识的方法。
    /// </summary>
    public interface IDatasetTransaction : IAsyncDisposable
    {
        /// <summary>
        /// 提交事务。
        /// </summary>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>表示操作完成的任务。</returns>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 回滚事务。
        /// </summary>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>表示操作完成的任务。</returns>
        Task RollbackAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取事务的唯一标识。
        /// </summary>
        string TransactionId { get; }
    }

    // ========== 扩展接口 ========== //
    /// <summary>
    /// 支持事务的数据集扩展接口，继承自 INonRelationalDataset<T> 接口，并添加了开始事务的方法。
    /// </summary>
    /// <typeparam name="T">数据集中元素的类型。</typeparam>
    public interface ITransactionalDataset<T> : INonRelationalDataset<T>
    {
        /// <summary>
        /// 开始一个新的事务，并返回事务对象。
        /// </summary>
        /// <param name="options">事务选项，包括隔离级别和超时时间等。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>事务对象，可用于提交或回滚事务。</returns>
        Task<IDatasetTransaction> BeginTransactionAsync(TransactionOptions? options = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 事务选项类，用于指定事务的隔离级别和超时时间等参数。
    /// </summary>
    public class TransactionOptions
    {
        public IsolationLevel IsolationLevel { get; set; } = IsolationLevel.Serializable;
        public TimeSpan? Timeout { get; set; }
    }
