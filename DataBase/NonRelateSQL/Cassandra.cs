using System.Linq.Expressions;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Org.BouncyCastle.Crypto;
using Server.DataBase.NonRelateSQL.Common;

namespace Server.DataBase.NonRelateSQL
{
    public class CassandraDataset<T> : INonRelationalDataset<T> where T : class
    {
        private readonly ISession _session;
        private readonly IMapper _mapper;
        private readonly string _tableName;

        public CassandraDataset(NonRelationalDatabaseConfig config)
        {
            var cluster = Cluster.Builder()
                .AddContactPoint(config.Host)
                .WithPort(config.Port)
                .WithCredentials(config.Username, config.Password)
                .WithQueryOptions(new QueryOptions()
                    .SetConsistencyLevel(ConsistencyLevel.One))
                .Build();

            _session = cluster.Connect(config.DatabaseName);
            _mapper = new Mapper(_session);
            _tableName = typeof(T).Name.ToLower();
        }

        public async Task<string> InsertAsync(T entity, CancellationToken cancellationToken = default)
        {
            //// 使用 Mapper 自动生成 CQL
            //await _mapper.InsertAsync(entity,
            //    insertNulls: false,
            //    ttl: 0);

            //// 获取实体ID的推荐方式（假设实体有Id属性）
            //return GetEntityId(entity);

            // 使用参数化查询
            var insertCql = new SimpleStatement(
                $"INSERT INTO {_tableName} (id, name) VALUES (?, ?)",
                GetEntityId(entity),
                (entity as dynamic).Name // 根据实际属性访问
            );

            await _session.ExecuteAsync(insertCql);
            return GetEntityId(entity);
        }

        public async Task<IReadOnlyList<string>> BulkInsertAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            var batch = new BatchStatement();
            var table = new Table<T>(_session);
            var ids = new List<string>();

            foreach (var entity in entities)
            {
                // 使用 LINQ 生成插入语句
                var insertStmt = table
                    .Insert(entity)
                    .SetConsistencyLevel(ConsistencyLevel.LocalQuorum)
                    .ToString();

                batch.Add(new SimpleStatement(insertStmt));
                ids.Add(GetEntityId(entity));
            }

            await _session.ExecuteAsync(batch);
            return ids.AsReadOnly();
        }

        public async Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            // 假设实体有 Id 属性作为主键
            return await _mapper.SingleOrDefaultAsync<T>($"WHERE id = ?", id);
        }

        public async Task<bool> UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            try
            {
                await _mapper.UpdateAsync(entity);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            // 假设实体有 Id 属性作为主键
            await _mapper.DeleteAsync<T>($"WHERE id = ?", id).ConfigureAwait(false);
            return true;
        }

        public IAsyncEnumerable<T> FindAllAsync(QueryOptions<T>? options = null, CancellationToken cancellationToken = default)
        {
            var query = _session.GetTable<T>();

            if (options?.Filter != null)
            {
                query = (Table<T>)query.Where(options.Filter);
            }

            if (options?.Sorting != null)
            {
                query = (Table<T>)(options.Sorting.IsAscending
                    ? query.OrderBy(options.Sorting.KeySelector)
                    : query.OrderByDescending(options.Sorting.KeySelector));
            }

            if (options?.Limit.HasValue == true)
            {
                query = (Table<T>)query.Take(options.Limit.Value);
            }

            return (IAsyncEnumerable<T>)query.ExecuteAsync();
        }

        public async Task<PaginatedResult<T>> PaginateAsync(PaginationOptions pagination, QueryOptions<T>? query = null, CancellationToken cancellationToken = default)
        {
            // Cassandra 的分页需要基于 token 实现
            var pageSize = Math.Min(pagination.PageSize, pagination.MaxPageSize);
            var statement = _session.GetTable<T>().SetPageSize(pageSize);

            // 添加查询条件
            if (query?.Filter != null)
            {
                statement = statement.Where(query.Filter);
            }

            var rs = await statement.ExecutePagedAsync();
            return new PaginatedResult<T>(
                rs,
                pagination.PageNumber,
                pageSize,
                rs.PagingState != null ? long.MaxValue : rs.Count()); // Cassandra 总数量需要单独查询
        }

        // 其他接口方法实现...

        private string GetEntityId(T entity)
        {
            // 实现根据实体获取 ID 的逻辑
            return entity.GetType().GetProperty("Id")?.GetValue(entity)?.ToString() ?? string.Empty;
        }

        public void Dispose() => _session.Dispose();

        public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ExistsAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task CreateIndexAsync(string indexName, IndexDefinition<T> definition, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task DropIndexAsync(string indexName, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}