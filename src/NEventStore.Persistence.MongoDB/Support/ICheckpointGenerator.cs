using System;
using System.Linq;
using System.Threading;
using MongoDB.Bson;
using MongoDB.Driver;

namespace NEventStore.Persistence.MongoDB.Support
{
    public interface ICheckpointGenerator
    {
        /// <summary>
        /// Generates a new checkpoint id
        /// </summary>
        /// <returns></returns>
        Int64 Next();

        /// <summary>
        /// The id generated is not valid, is duplicated. It is necessary
        /// when there are multiple processes that generates id in autonomous way
        /// </summary>
        /// <param name="id"></param>
        void SignalDuplicateId(Int64 id);
    }

    public class InMemoryCheckpointGenerator : ICheckpointGenerator
    {
        protected Int64 _last;

        protected FilterDefinition<BsonDocument> Filter { get; }

        protected FindOptions<BsonDocument, BsonDocument> FindOptions { get; }

        private readonly IMongoCollection<BsonDocument> _collection;

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

        protected Int64 GetLastValue()
        {
            var max = _collection
               .FindSync(Filter, FindOptions)
               .FirstOrDefault();

            return max?[MongoCommitFields.CheckpointNumber].AsInt64 ?? 0L;
        }

        public virtual long Next()
        {
            return Interlocked.Increment(ref _last);
        }

        public virtual void SignalDuplicateId(long id)
        {
            _last = GetLastValue();
        }
    }

    public class AlwaysQueryDbForNextValueCheckpointGenerator : InMemoryCheckpointGenerator
    {
        public AlwaysQueryDbForNextValueCheckpointGenerator(IMongoCollection<BsonDocument> collection)
            : base(collection)
        {
        }

        public override long Next()
        {
            return _last = base.GetLastValue() + 1;
        }

        public override void SignalDuplicateId(long id)
        {
            _last = base.GetLastValue();
        }
    }
}
