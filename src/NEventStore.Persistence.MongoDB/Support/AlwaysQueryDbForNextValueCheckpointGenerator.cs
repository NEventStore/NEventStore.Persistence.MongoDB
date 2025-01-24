using MongoDB.Bson;
using MongoDB.Driver;

namespace NEventStore.Persistence.MongoDB.Support
{
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
        public override async Task<long> NextAsync(CancellationToken cancellationToken)
        {
            return _last = await GetLastValueAsync(cancellationToken).ConfigureAwait(false) + 1;
        }
    }
}
