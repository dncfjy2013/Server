using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Nest;
using Server.DataBase.Common;

namespace Server.DataBase.NonRelateSQL
{
    public class ElasticsearchDataset<T> : INonRelationalDataset<T> where T : class
    {
        private readonly IElasticClient _client;
        private readonly string _indexName;

        public ElasticsearchDataset(NonRelationalDatabaseConfig config)
        {
            var settings = new ConnectionSettings(new Uri($"http://{config.Host}:{config.Port}"))
                .DefaultIndex(_indexName)
                .BasicAuthentication(config.Username, config.Password)
                .EnableApiVersioningHeader();

            if (!string.IsNullOrEmpty(config.ElasticsearchApiKey))
                settings.ApiKeyAuthentication(config.ElasticsearchCloudId, config.ElasticsearchApiKey);

            _client = new ElasticClient(settings);
            _indexName = config.DatabaseName.ToLower();
        }

        public async Task<string> InsertAsync(T entity, CancellationToken cancellationToken = default)
        {
            var response = await _client.IndexAsync(entity, idx => idx.Index(_indexName), cancellationToken);
            return response.Id;
        }

        public async Task<IReadOnlyList<string>> BulkInsertAsync(IEnumerable<T> entities, CancellationToken cancellationToken = default)
        {
            var bulkResponse = await _client.BulkAsync(b => b
                .Index(_indexName)
                .IndexMany(entities), cancellationToken);

            return bulkResponse.Items
                .Where(item => item.IsValid)
                .Select(item => item.Id)
                .ToList();
        }

        public async Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            var response = await _client.GetAsync<T>(id, g => g.Index(_indexName), cancellationToken);
            return response.Source;
        }

        public async Task<bool> UpdateAsync(T entity, CancellationToken cancellationToken = default)
        {
            var response = await _client.UpdateAsync<T>(entity, u => u.Index(_indexName), cancellationToken);
            return response.IsValid;
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var response = await _client.DeleteAsync<T>(id, d => d.Index(_indexName), cancellationToken);
            return response.IsValid;
        }

        public async IAsyncEnumerable<T> FindAllAsync(QueryOptions<T>? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var searchDescriptor = BuildSearchDescriptor(options);

            var response = await _client.SearchAsync<T>(s => searchDescriptor, cancellationToken);
            foreach (var document in response.Documents)
            {
                yield return document;
            }
        }

        public async Task<PaginatedResult<T>> PaginateAsync(PaginationOptions pagination, QueryOptions<T>? query = null, CancellationToken cancellationToken = default)
        {
            var searchDescriptor = BuildSearchDescriptor(query)
                .From((pagination.PageNumber - 1) * pagination.PageSize)
                .Size(pagination.PageSize);

            var response = await _client.SearchAsync<T>(s => searchDescriptor, cancellationToken);

            return new PaginatedResult<T>(
                response.Documents,
                pagination.PageNumber,
                pagination.PageSize,
                response.Total);
        }

        public async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken cancellationToken = default)
        {
            var countResponse = await _client.CountAsync<T>(c => c
                .Index(_indexName)
                .Query(q => filter == null ? q.MatchAll() : q.QueryString(qs => qs.Query(filter.ToString()))),
                cancellationToken);

            return countResponse.Count;
        }

        public async Task<bool> ExistsAsync(Expression<Func<T, bool>> filter, CancellationToken cancellationToken = default)
        {
            var count = await CountAsync(filter, cancellationToken);
            return count > 0;
        }

        public async Task CreateIndexAsync(string indexName, IndexDefinition<T> definition, CancellationToken cancellationToken = default)
        {
            var createIndexResponse = await _client.Indices.CreateAsync(indexName, c => c
                .Map<T>(m => m.AutoMap())
                .Settings(s => s
                    .NumberOfShards(1)
                    .NumberOfReplicas(0)),
                cancellationToken);
        }

        public async Task DropIndexAsync(string indexName, CancellationToken cancellationToken = default)
        {
            await _client.Indices.DeleteAsync(indexName, null, cancellationToken);
        }

        private SearchDescriptor<T> BuildSearchDescriptor(QueryOptions<T>? options)
        {
            var descriptor = new SearchDescriptor<T>()
                .Index(_indexName);

            if (options?.Filter != null)
            {
                descriptor = descriptor.Query(q => q
                    .Bool(b => b
                        .Filter(f => f
                            .QueryString(qs => qs
                                .Query(options.Filter.ToString())))));
            }

            if (options?.Sorting != null)
            {
                descriptor = options.Sorting.IsAscending
                    ? descriptor.Sort(s => s.Ascending(options.Sorting.KeySelector))
                    : descriptor.Sort(s => s.Descending(options.Sorting.KeySelector));
            }

            if (options?.Limit.HasValue == true)
            {
                descriptor = descriptor.Size(options.Limit.Value);
            }

            return descriptor;
        }

        public void Dispose()
        {
            // ElasticClient doesn't require disposal
        }
    }
}