using System.Text.Json;
using StackExchange.Redis;
using System.Linq.Expressions;
using global::Server.DataBase.NonRelateSQL.Common;
using System.Runtime.CompilerServices;

namespace Server.DataBase.NonRelateSQL
{
    public class RedisDataset<T> : ITransactionalDataset<T> where T : class
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly string _keyPrefix;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        public RedisDataset(NonRelationalDatabaseConfig config)
        {
            var options = new ConfigurationOptions
            {
                EndPoints = { { config.Host, config.Port } },
                Password = config.Password,
                ConnectTimeout = config.RedisConnectTimeout,
                SyncTimeout = config.RedisSyncTimeout,
                AbortOnConnectFail = false,
                AsyncTimeout = 5000,
                ConnectRetry = 3,
                KeepAlive = 180,
                AllowAdmin = true
            };

            _redis = ConnectionMultiplexer.Connect(options);
            _db = _redis.GetDatabase(config.RedisDatabaseIndex);
            _keyPrefix = $"{config.DatabaseName}:{typeof(T).Name}:";
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public async Task<string> InsertAsync(T entity, CancellationToken cancellationToken = default)
        {
            var id = Guid.NewGuid().ToString();
            var key = GetKey(id);
            var json = JsonSerializer.Serialize(entity, _jsonOptions);

            await _db.StringSetAsync(key, json);
            await MaintainKeyIndex(id);
            return id;
        }

        public async Task<IReadOnlyList<string>> BulkInsertAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            var ids = new List<string>();
            var batch = _db.CreateBatch();

            foreach (var entity in entities)
            {
                var id = Guid.NewGuid().ToString();
                var key = GetKey(id);
                var json = JsonSerializer.Serialize(entity, _jsonOptions);

                batch.StringSetAsync(key, json);
                await MaintainKeyIndex(id);
                ids.Add(id);
            }

            batch.Execute();
            return ids.AsReadOnly();
        }

        public async Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            var json = await _db.StringGetAsync(GetKey(id));
            return json.HasValue
                ? JsonSerializer.Deserialize<T>(json!, _jsonOptions)
                : null;
        }

        public async Task<bool> UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            // 需要实现ID字段访问，此处假设实体有Id属性
            var id = (string)entity.GetType().GetProperty("Id")!.GetValue(entity)!;
            var key = GetKey(id);
            var json = JsonSerializer.Serialize(entity, _jsonOptions);

            return await _db.StringSetAsync(key, json);
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            await RemoveKeyIndex(id);
            return await _db.KeyDeleteAsync(GetKey(id));
        }

        public async IAsyncEnumerable<T> FindAllAsync(QueryOptions<T>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var allKeys = await GetAllKeys();
            var items = new List<T>();

            foreach (var key in allKeys)
            {
                var id = GetIdFromKey(key);
                var item = await GetByIdAsync(id, cancellationToken);

                if (item != null)
                {
                    // 应用筛选条件
                    if (options?.Filter != null)
                    {
                        var compiledFilter = options.Filter.Compile();
                        if (!compiledFilter(item))
                        {
                            continue;
                        }
                    }

                    items.Add(item);
                }
            }

            // 应用排序
            if (options?.Sorting != null)
            {
                var keySelector = options.Sorting.KeySelector.Compile();
                if (options.Sorting.IsAscending)
                {
                    items = items.OrderBy(keySelector).ToList();
                }
                else
                {
                    items = items.OrderByDescending(keySelector).ToList();
                }
            }

            foreach (var item in items)
            {
                yield return item;
            }
        }

        public async Task<PaginatedResult<T>> PaginateAsync(PaginationOptions pagination, QueryOptions<T>? query = null, CancellationToken cancellationToken = default)
        {
            // 实现分页逻辑
            var allKeys = await GetAllKeys();
            var totalCount = allKeys.Length;
            var pageKeys = allKeys
                .Skip((pagination.PageNumber - 1) * pagination.PageSize)
                .Take(pagination.PageSize);

            var values = await _db.StringGetAsync(pageKeys.Select(k => (RedisKey)k).ToArray());

            var items = values
                .Where(v => v.HasValue)
                .Select(v => JsonSerializer.Deserialize<T>(v!, _jsonOptions)!);

            return new PaginatedResult<T>(
                items,
                pagination.PageNumber,
                pagination.PageSize,
                totalCount
            );
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
        {
            var keys = await GetAllKeys();
            return keys.Length;
        }

        public async Task<bool> ExistsAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            var allKeys = await GetAllKeys();
            var compiledFilter = filter.Compile();

            foreach (var key in allKeys)
            {
                var item = await GetByIdAsync(GetIdFromKey(key));
                if (item != null && compiledFilter(item))
                {
                    return true;
                }
            }

            return false;
        }

        public async Task CreateIndexAsync(string indexName, IndexDefinition<T> definition, CancellationToken cancellationToken = default)
        {
            // 使用Redis有序集合实现索引
            var indexKey = $"{_keyPrefix}index:{indexName}";
            var batch = _db.CreateBatch();

            foreach (var key in await GetAllKeys())
            {
                var item = await GetByIdAsync(GetIdFromKey(key));
                var value = definition.KeyExpression.Compile()(item!);
                batch.SortedSetAddAsync(indexKey, key, Convert.ToDouble(value));
            }

            batch.Execute();
        }

        public async Task DropIndexAsync(string indexName, CancellationToken cancellationToken = default)
        {
            await _db.KeyDeleteAsync($"{_keyPrefix}index:{indexName}");
        }

        public async Task<IDatasetTransaction> BeginTransactionAsync(TransactionOptions? options = null, CancellationToken cancellationToken = default)
        {
            await _connectionLock.WaitAsync();
            try
            {
                var tran = _db.CreateTransaction();
                return new RedisTransaction(tran);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        private async Task MaintainKeyIndex(string id)
        {
            await _db.SetAddAsync($"{_keyPrefix}_keys", id);
        }

        private async Task RemoveKeyIndex(string id)
        {
            await _db.SetRemoveAsync($"{_keyPrefix}_keys", id);
        }

        private async Task<string[]> GetAllKeys()
        {
            var members = await _db.SetMembersAsync($"{_keyPrefix}_keys");
            return members.Select(m => m.ToString()).ToArray();
        }

        private string GetKey(string id) => $"{_keyPrefix}{id}";
        private string GetIdFromKey(string key) => key.Replace(_keyPrefix, "");

        public async ValueTask DisposeAsync()
        {
            _redis.Close();
            _redis.Dispose();
            GC.SuppressFinalize(this);
        }

        private class RedisTransaction : IDatasetTransaction
        {
            private readonly ITransaction _transaction;

            public RedisTransaction(ITransaction transaction)
            {
                _transaction = transaction;
                TransactionId = Guid.NewGuid().ToString();
            }

            public string TransactionId { get; }

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                await _transaction.ExecuteAsync();
            }

            public async Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                // Redis事务无法回滚，只能丢弃
            }

            public ValueTask DisposeAsync() => default;
        }
    }

}
