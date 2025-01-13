using MongoDB.Bson.Serialization.Attributes;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization.Options;

namespace NEventStore.Persistence.MongoDB
{
    /// <summary>
    /// Represents a commit in MongoDB.
    /// </summary>
    /// <remarks>
    /// let's ignore the extra elements, the 'Dispatched' field and the dispatched concept have been dropped in NEventStore 6
    /// </remarks>
    [BsonIgnoreExtraElements]
    public class MongoCommit
    {
        /// <summary>
        /// Gets or sets the checkpoint number.
        /// </summary>
        [BsonId]
        [BsonElement(MongoCommitFields.CheckpointNumber)]
        public long CheckpointNumber { get; set; }

        /// <summary>
        /// Gets or sets the commit identifier.
        /// </summary>
        [BsonElement(MongoCommitFields.CommitId)]
        public Guid CommitId { get; set; }

        /// <summary>
        /// Gets or sets the commit timestamp.
        /// </summary>
        [BsonElement(MongoCommitFields.CommitStamp)]
        public DateTime CommitStamp { get; set; }

        /// <summary>
        /// Gets or sets the commit headers.
        /// </summary>
        /// <remarks>
        /// we can override this specifying a ClassMap OR implementing an <see cref="global::MongoDB.Bson.Serialization.IBsonSerializer"/>
        /// and an <see cref="global::MongoDB.Bson.Serialization.IBsonSerializationProvider"/>
        /// </remarks>
        [BsonElement(MongoCommitFields.Headers)]
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfArrays)]
        public IDictionary<string, object> Headers { get; set; }

        /// <summary>
        /// Gets or sets the events.
        /// </summary>
        [BsonElement(MongoCommitFields.Events)]
        public BsonArray Events { get; set; } // multiple evaluations using linq can be dangerous, maybe it's better have a plain array to avoid bugs

        /// <summary>
        /// Gets or sets the stream revision from.
        /// </summary>
        [BsonElement(MongoCommitFields.StreamRevisionFrom)]
        public int StreamRevisionFrom { get; set; }

        /// <summary>
        /// Gets or sets the stream revision to.
        /// </summary>
        [BsonElement(MongoCommitFields.StreamRevisionTo)]
        public int StreamRevisionTo { get; set; }

        /// <summary>
        /// Gets or sets the bucket identifier.
        /// </summary>
        [BsonElement(MongoCommitFields.BucketId)]
        public string BucketId { get; set; }

        /// <summary>
        /// Gets or sets the stream identifier.
        /// </summary>
        [BsonElement(MongoCommitFields.StreamId)]
        public string StreamId { get; set; }

        /// <summary>
        /// Gets or sets the commit sequence.
        /// </summary>
        [BsonElement(MongoCommitFields.CommitSequence)]
        public int CommitSequence { get; set; }
    }
}