using MongoDB.Bson;
using MongoDB.Driver;

namespace NEventStore.Persistence.MongoDB.Support
{
    /// <summary>
    /// Generates a new checkpoint id
    /// </summary>
    public interface ICheckpointGenerator
    {
        /// <summary>
        /// Generates a new checkpoint id
        /// </summary>
        Int64 Next();

        /// <summary>
        /// Generates a new checkpoint id
        /// </summary>
        Task<Int64> NextAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The id generated is not valid, it is duplicated.
        /// We might need to reinitialize the checkpoint generator; iIt is necessary
        /// when there are multiple processes that generates id in autonomous way.
        /// </summary>
        void SignalDuplicateId(Int64 id);

        /// <summary>
        /// The id generated is not valid, it is duplicated.
        /// We might need to reinitialize the checkpoint generator; iIt is necessary
        /// when there are multiple processes that generates id in autonomous way.
        /// </summary>
        Task SignalDuplicateIdAsync(Int64 id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// In-memory Checkpoint Generator:
    /// - Reads the last checkpoint id from the database
    /// - Generates the next checkpoint id in memory
    /// </summary>
    public class InMemoryCheckpointGenerator : ICheckpointGenerator
    {
        /// <summary>
        /// The last generated id
        /// </summary>
        protected Int64 _last;

        /// <summary>
        /// The filter to get the last checkpoint id
        /// </summary>
        protected FilterDefinition<BsonDocument> Filter { get; }

        /// <summary>
        /// The options to get the last checkpoint id
        /// </summary>
        protected FindOptions<BsonDocument, BsonDocument> FindOptions { get; }

        private readonly IMongoCollection<BsonDocument> _collection;

        /// <summary>
        /// Initializes a new instance of the InMemoryCheckpointGenerator class.
        /// </summary>
        public InMemoryCheckpointGenerator(IMongoCollection<BsonDocument> collection)
        {
            _collection = collection;
            Filter = Builders<BsonDocument>.Filter.Empty;
            FindOptions = new FindOptions<BsonDocument, BsonDocument>()
            {
                Limit = 1,
                Projection = Builders<BsonDocument>.Projection.Include(MongoCommitFields.CheckpointNumber),
                Sort = Builders<BsonDocument>.Sort.Descending(MongoCommitFields.CheckpointNumber)
            };
            _last = GetLastValue();
        }

        /// <summary>
        /// Get the last value from the database
        /// </summary>
        protected Int64 GetLastValue()
        {
            var max = _collection
               .FindSync(Filter, FindOptions)
               .FirstOrDefault();

            return max?[MongoCommitFields.CheckpointNumber].AsInt64 ?? 0L;
        }

        /// <summary>
        /// Get the last value from the database
        /// </summary>
        protected async Task<Int64> GetLastValueAsync(CancellationToken cancellationToken = default)
        {
            var max = await _collection
               .FindSync(Filter, FindOptions, cancellationToken)
               .FirstOrDefaultAsync(cancellationToken)
               .ConfigureAwait(false);

            return max?[MongoCommitFields.CheckpointNumber].AsInt64 ?? 0L;
        }

        /// <inheritdoc />
        public virtual long Next()
        {
            return Interlocked.Increment(ref _last);
        }

        /// <inheritdoc />
        public virtual void SignalDuplicateId(long id)
        {
            _last = GetLastValue();
        }

        /// <inheritdoc />
        public virtual Task<long> NextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Next());
        }

        /// <inheritdoc />
        public virtual async Task SignalDuplicateIdAsync(long id, CancellationToken cancellationToken = default)
        {
            _last = await GetLastValueAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A checkpoint generator that always queries the database for the next value.
    /// </summary>
    public class AlwaysQueryDbForNextValueCheckpointGenerator : InMemoryCheckpointGenerator
    {
        /// <summary>
        /// Initializes a new instance of the AlwaysQueryDbForNextValueCheckpointGenerator class.
        /// </summary>
        public AlwaysQueryDbForNextValueCheckpointGenerator(IMongoCollection<BsonDocument> collection)
            : base(collection)
        {
        }

        /// <inheritdoc />
        public override long Next()
        {
            return _last = GetLastValue() + 1;
        }

        /// <inheritdoc />
        public override async Task<long> NextAsync(CancellationToken cancellationToken = default)
        {
            return _last = await GetLastValueAsync(cancellationToken).ConfigureAwait(false) + 1;
        }
    }
}
