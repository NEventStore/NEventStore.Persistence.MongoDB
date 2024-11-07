using System;
using System.Linq;
using System.Threading;
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
        /// The id generated is not valid, is duplicated. It is necessary
        /// when there are multiple processes that generates id in autonomous way
        /// </summary>
        void SignalDuplicateId(Int64 id);
    }

    /// <summary>
    /// In-memory Checkpoint Generator
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
    }

    /// <summary>
    /// A checkpoint generator that always queries the database for the next value.
    /// </summary>
    public class AlwaysQueryDbForNextValueCheckpointGenerator : InMemoryCheckpointGenerator
    {
        /// <summary>
        /// Initializes a new instance of the AlwaysQueryDbForNextValueCheckpointGenerator class.
        /// </summary>
        /// <param name="collection"></param>
        public AlwaysQueryDbForNextValueCheckpointGenerator(IMongoCollection<BsonDocument> collection)
            : base(collection)
        {
        }

        /// <inheritdoc />
        public override long Next()
        {
            return _last = base.GetLastValue() + 1;
        }

        /// <inheritdoc />
        public override void SignalDuplicateId(long id)
        {
            _last = base.GetLastValue();
        }
    }
}
