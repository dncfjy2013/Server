using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Server.DataBase.NonRelateSQL.Common;

namespace Server.DataBase.NonRelational.MongoDB
{
    public class MongoDataset<T> : ITransactionalDataset<T> where T : class
    {
        private readonly IMongoCollection<T> _collection;
        private readonly IMongoDatabase _database;

        public MongoDataset(NonRelationalDatabaseConfig config)
        {
            var settings = new MongoClientSettings
            {
                Server = new MongoServerAddress(config.Host, config.Port),
                Credential = MongoCredential.CreateCredential(
                    config.DatabaseName,
                    config.Username,
                    config.Password)
            };

            var client = new MongoClient(settings);
            _database = client.GetDatabase(config.DatabaseName);
            _collection = _database.GetCollection<T>(typeof(T).Name);
        }

        // ========== 核心CRUD操作 ========== //
        public async Task<string> InsertAsync(T entity, CancellationToken cancellationToken = default)
        {
            await _collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
            return GetIdValue(entity).ToString();
        }

        public async Task<IReadOnlyList<string>> BulkInsertAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            var writeModels = entities.Select(e => new InsertOneModel<T>(e));
            await _collection.BulkWriteAsync(writeModels, cancellationToken: cancellationToken);
            return entities.Select(GetIdValue).Select(x => x.ToString()).ToList();
        }

        public async Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            var filter = BuildIdFilter(id);
            return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<bool> UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            var filter = BuildIdFilter(GetIdValue(entity).ToString());
            var result = await _collection.ReplaceOneAsync(filter, entity, cancellationToken: cancellationToken);
            return result.IsAcknowledged && result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var filter = BuildIdFilter(id);
            var result = await _collection.DeleteOneAsync(filter, cancellationToken);
            return result.IsAcknowledged && result.DeletedCount > 0;
        }

        // ========== 高级查询 ========== //
        public async IAsyncEnumerable<T> FindAllAsync(QueryOptions<T>? options = null, CancellationToken cancellationToken = default)
        {
            var query = BuildQuery(options);
            using var cursor = await query.ToCursorAsync(cancellationToken);
            while (await cursor.MoveNextAsync(cancellationToken))
            {
                foreach (var item in cursor.Current)
                {
                    yield return item;
                }
            }
        }

        public async Task<PaginatedResult<T>> PaginateAsync(PaginationOptions pagination, QueryOptions<T>? queryOptions = null, CancellationToken cancellationToken = default)
        {
            var query = BuildQuery(queryOptions);
            var count = await query.CountDocumentsAsync(cancellationToken);

            var items = await query
                .Skip((pagination.PageNumber - 1) * pagination.PageSize)
                .Limit(pagination.PageSize)
                .ToListAsync(cancellationToken);

            return new PaginatedResult<T>(
                items,
                pagination.PageNumber,
                pagination.PageSize,
                count);
        }

        // ========== 聚合操作 ========== //
        public async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
        {
            var query = filter != null
                ? _collection.Find(filter)
                : _collection.Find(FilterDefinition<T>.Empty);

            return await query.CountDocumentsAsync(cancellationToken);
        }

        public async Task<bool> ExistsAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            return await _collection.Find(filter).AnyAsync(cancellationToken);
        }

        // ========== 索引创建方法优化 ========== //
        public async Task CreateIndexAsync(
            string indexName,
            IndexDefinition<T> definition,
            CancellationToken cancellationToken = default)
        {
            // 获取成员名称
            var memberExpression = definition.KeyExpression.Body as MemberExpression;
            if (memberExpression == null)
                throw new ArgumentException("Invalid key selector expression");

            var fieldName = GetMongoFieldName(memberExpression.Member);

            // 构建索引键
            var keys = Builders<T>.IndexKeys.Ascending(fieldName);

            var options = new CreateIndexOptions
            {
                Name = indexName,
                Unique = definition.IsUnique,
                Background = true // 添加后台构建优化
            };

            await _collection.Indexes.CreateOneAsync(
                new CreateIndexModel<T>(keys, options),
                cancellationToken: cancellationToken);
        }

        // 获取 MongoDB 字段名
        private string GetMongoFieldName(MemberInfo member)
        {
            // 优先使用 BsonElement 特性
            var bsonElement = member.GetCustomAttribute<BsonElementAttribute>();
            if (bsonElement != null) return bsonElement.ElementName;

            // 其次使用 BsonId 特性
            var bsonId = member.GetCustomAttribute<BsonIdAttribute>();
            if (bsonId != null) return "_id";

            // 默认使用驼峰命名转换
            return char.ToLowerInvariant(member.Name[0]) + member.Name[1..];
        }


        public async Task DropIndexAsync(string indexName, CancellationToken cancellationToken = default)
        {
            await _collection.Indexes.DropOneAsync(indexName, cancellationToken);
        }

        // ========== 事务处理 ========== //
        public async Task<IDatasetTransaction> BeginTransactionAsync(TransactionOptions? options = null, CancellationToken cancellationToken = default)
        {
            var session = await _database.Client.StartSessionAsync(
                new ClientSessionOptions(),
                cancellationToken);

            session.StartTransaction();

            return new MongoTransaction(session);
        }

        public void Dispose() => _database.Client.Cluster.Dispose();

        // ========== 私有方法 ========== //
        private IFindFluent<T, T> BuildQuery(QueryOptions<T>? options)
        {
            var query = _collection.Find(options?.Filter ?? FilterDefinition<T>.Empty);

            if (options?.Sorting != null)
            {
                var sort = options.Sorting.IsAscending
                    ? Builders<T>.Sort.Ascending(options.Sorting.KeySelector)
                    : Builders<T>.Sort.Descending(options.Sorting.KeySelector);
                query = query.Sort(sort);
            }

            if (options?.Limit.HasValue == true)
            {
                query = query.Limit(options.Limit.Value);
            }

            return query;
        }

        private static FilterDefinition<T> BuildIdFilter(string id)
        {
            return Builders<T>.Filter.Eq("_id", ObjectId.Parse(id));
        }

        private static ObjectId GetIdValue(T entity)
        {
            var bsonId = entity.ToBsonDocument()["_id"];
            return bsonId.AsObjectId;
        }

        private class MongoTransaction : IDatasetTransaction
        {
            private readonly IClientSessionHandle _session;

            public MongoTransaction(IClientSessionHandle session)
            {
                _session = session;
                TransactionId = session.ServerSession.Id.ToString();
            }

            public string TransactionId { get; }

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                await _session.CommitTransactionAsync(cancellationToken);
            }

            public async Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                await _session.AbortTransactionAsync(cancellationToken);
            }

            public void Dispose()
            {
                _session.Dispose();
            }
        }
    }
}