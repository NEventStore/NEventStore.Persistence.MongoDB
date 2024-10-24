using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization.Options;
using global::MongoDB.Driver;
using NEventStore.Serialization;
using BsonSerializer = global::MongoDB.Bson.Serialization.BsonSerializer;

namespace NEventStore.Persistence.MongoDB
{
    /// <summary>
    /// Extension methods for working with MongoDB.
    /// </summary>
    public static class ExtensionMethods
    {
        /// <summary>
        /// Converts a <see cref="CommitAttempt"/> to a <see cref="BsonDocument"/>.
        /// </summary>
        public static BsonDocument ToMongoCommit(this CommitAttempt commit, Int64 checkpoint, IDocumentSerializer serializer)
        {
            int streamRevision = commit.StreamRevision - (commit.Events.Count - 1);
            int streamRevisionStart = streamRevision;

            IEnumerable<BsonDocument> events = commit
                .Events
                .Select(e =>
                    new BsonDocument
                    {
                        {MongoCommitFields.StreamRevision, streamRevision++},
                        {MongoCommitFields.Payload, BsonDocumentWrapper.Create(typeof(EventMessage), serializer.Serialize(e))}
                    });

            var mc = new MongoCommit
            {
                CheckpointNumber = checkpoint,
                CommitId = commit.CommitId,
                CommitStamp = commit.CommitStamp,
                Headers = commit.Headers,
                Events = new BsonArray(events),
                StreamRevisionFrom = streamRevisionStart,
                StreamRevisionTo = streamRevision - 1,
                BucketId = commit.BucketId,
                StreamId = commit.StreamId,
                CommitSequence = commit.CommitSequence
            };

            return mc.ToBsonDocument();
        }

        /// <summary>
        /// Converts a <see cref="CommitAttempt"/> to a <see cref="BsonDocument"/> representing an empty commit.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static BsonDocument ToEmptyCommit(this CommitAttempt commit, Int64 checkpoint, String systemBucketName)
        {
            if (commit == null) throw new ArgumentNullException(nameof(commit));
            if (String.IsNullOrWhiteSpace(systemBucketName)) throw new ArgumentNullException(nameof(systemBucketName));
            int streamRevisionStart = commit.StreamRevision - (commit.Events.Count - 1);

            var mc = new MongoCommit
            {
                CheckpointNumber = checkpoint,
                CommitId = commit.CommitId,
                CommitStamp = commit.CommitStamp,
                Headers = new Dictionary<String, Object>(),
                Events = new BsonArray(Array.Empty<object>()),
                StreamRevisionFrom = 0,
                StreamRevisionTo = 0,
                BucketId = systemBucketName,
                StreamId = systemBucketName + ".empty." + checkpoint,
                CommitSequence = 1
            };

            return mc.ToBsonDocument();
        }

        /// <summary>
        /// Converts a <see cref="BsonDocument"/> to a <see cref="ICommit"/>.
        /// </summary>
        public static ICommit ToCommit(this BsonDocument doc, IDocumentSerializer serializer)
        {
            if (doc == null)
            {
                return null;
            }

            var mc = BsonSerializer.Deserialize<MongoCommit>(doc);

            return new Commit(mc.BucketId,
                mc.StreamId,
                mc.StreamRevisionTo,
                mc.CommitId,
                mc.CommitSequence,
                mc.CommitStamp,
                mc.CheckpointNumber,
                mc.Headers,
                mc.Events.Select(e =>
                {
                    BsonValue payload = e[MongoCommitFields.Payload];
                    return payload.IsBsonDocument
                           ? BsonSerializer.Deserialize<EventMessage>(payload.ToBsonDocument())
                           : serializer.Deserialize<EventMessage>(payload.AsByteArray); // ByteStreamDocumentSerializer ?!?! doesn't work this way!
                }).ToArray());
        }

        /// <summary>
        /// Converts a <see cref="ISnapshot"/> to a <see cref="BsonDocument"/>.
        /// </summary>
        public static BsonDocument ToMongoSnapshot(this ISnapshot snapshot, IDocumentSerializer serializer)
        {
            return new BsonDocument
            {
                { MongoShapshotFields.Id, new BsonDocument
                    {
                        {MongoShapshotFields.BucketId, snapshot.BucketId},
                        {MongoShapshotFields.StreamId, snapshot.StreamId},
                        {MongoShapshotFields.StreamRevision, snapshot.StreamRevision}
                    }
                },
                { MongoShapshotFields.Payload, BsonDocumentWrapper.Create(serializer.Serialize(snapshot.Payload)) }
            };
        }

        /// <summary>
        /// Converts a <see cref="BsonDocument"/> to a <see cref="ISnapshot"/>.
        /// </summary>
        public static Snapshot ToSnapshot(this BsonDocument doc, IDocumentSerializer serializer)
        {
            if (doc == null)
            {
                return null;
            }

            BsonDocument id = doc[MongoShapshotFields.Id].AsBsonDocument;
            string bucketId = id[MongoShapshotFields.BucketId].AsString;
            string streamId = id[MongoShapshotFields.StreamId].AsString;
            int streamRevision = id[MongoShapshotFields.StreamRevision].AsInt32;
            BsonValue bsonPayload = doc[MongoShapshotFields.Payload];

            object payload;
            switch (bsonPayload.BsonType)
            {
                case BsonType.Binary:
                    payload = serializer.Deserialize<object>(bsonPayload.AsByteArray);
                    break;
                case BsonType.Document:
                    payload = BsonSerializer.Deserialize<object>(bsonPayload.AsBsonDocument);
                    break;
                default:
                    payload = BsonTypeMapper.MapToDotNetValue(bsonPayload);
                    break;
            }

            return new Snapshot(bucketId, streamId, streamRevision, payload);
        }

        /// <summary>
        /// Converts a <see cref="BsonDocument"/> to a <see cref="StreamHead"/>.
        /// </summary>
        public static StreamHead ToStreamHead(this BsonDocument doc)
        {
            BsonDocument id = doc[MongoStreamHeadFields.Id].AsBsonDocument;
            string bucketId = id[MongoStreamHeadFields.BucketId].AsString;
            string streamId = id[MongoStreamHeadFields.StreamId].AsString;
            return new StreamHead(bucketId, streamId, doc[MongoStreamHeadFields.HeadRevision].AsInt32, doc[MongoStreamHeadFields.SnapshotRevision].AsInt32);
        }

        /// <summary>
        /// Returns a query that can be used to find a commit given the information of the <see cref="CommitAttempt"/>.
        /// </summary>
        public static FilterDefinition<BsonDocument> ToMongoCommitIdQuery(this CommitAttempt commit)
        {
            var builder = Builders<BsonDocument>.Filter;
            return builder.And(
                builder.Eq(MongoCommitFields.BucketId, commit.BucketId),
                builder.Eq(MongoCommitFields.StreamId, commit.StreamId),
                builder.Eq(MongoCommitFields.CommitId, commit.CommitId)
            );
        }

        /// <summary>
        /// Builds a query that can be used to find the snapshot for a stream.
        /// </summary>
        public static FilterDefinition<BsonDocument> GetSnapshotQuery(string bucketId, string streamId, int maxRevision)
        {
            var builder = Builders<BsonDocument>.Filter;

            return builder.And(
                builder.Eq(MongoShapshotFields.FullQualifiedBucketId, bucketId),
                builder.Eq(MongoShapshotFields.FullQualifiedStreamId, streamId),
                builder.Lte(MongoShapshotFields.FullQualifiedStreamRevision, maxRevision)
            );
        }
    }

    /// <summary>
    /// Represents a commit in MongoDB.
    /// </summary>
    /// <remarks>
    /// let's ignore the extra elements, the 'Dispatched' field and the dispatched concept have been dropped in NEventStore 6
    /// </remarks>
    [BsonIgnoreExtraElements]
    public sealed class MongoCommit
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