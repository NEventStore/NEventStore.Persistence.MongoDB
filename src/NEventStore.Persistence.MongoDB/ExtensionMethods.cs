using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Serialization;
using BsonSerializer = MongoDB.Bson.Serialization.BsonSerializer;

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
            int eventCount = commit.Events.Count;
            int streamRevision = commit.StreamRevision - (eventCount - 1);
            int streamRevisionStart = streamRevision;

            var events = new BsonArray(eventCount);
            foreach (var @event in commit.Events)
            {
                events.Add(
                    new BsonDocument
                    {
                        { MongoCommitFields.StreamRevision, streamRevision++ },
                        { MongoCommitFields.Payload, BsonDocumentWrapper.Create(typeof(EventMessage), serializer.Serialize(@event)) }
                    });
            }

            var mc = new MongoCommit
            {
                CheckpointNumber = checkpoint,
                CommitId = commit.CommitId,
                CommitStamp = commit.CommitStamp,
                Headers = commit.Headers,
                Events = events,
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
            if (String.IsNullOrWhiteSpace(systemBucketName)) throw new ArgumentNullException(nameof(systemBucketName));

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
            var mc = BsonSerializer.Deserialize<MongoCommit>(doc);
            int eventCount = mc.Events.Count;
            var events = new EventMessage[eventCount];

            for (int i = 0; i < eventCount; i++)
            {
                BsonValue payload = mc.Events[i][MongoCommitFields.Payload];
                events[i] = payload.IsBsonDocument
                    ? BsonSerializer.Deserialize<EventMessage>(payload.ToBsonDocument())!
                    : serializer.Deserialize<EventMessage>(payload.AsByteArray)!; // ByteStreamDocumentSerializer ?!?! doesn't work this way!
            }

            return new Commit(mc.BucketId,
                mc.StreamId,
                mc.StreamRevisionTo,
                mc.CommitId,
                mc.CommitSequence,
                mc.CommitStamp,
                mc.CheckpointNumber,
                mc.Headers,
                events);
        }

        /// <summary>
        /// Converts a <see cref="ISnapshot"/> to a <see cref="BsonDocument"/>.
        /// </summary>
        public static BsonDocument ToMongoSnapshot(this ISnapshot snapshot, IDocumentSerializer serializer)
        {
            return new BsonDocument
            {
                { MongoSnapshotFields.Id, new BsonDocument
                    {
                        {MongoSnapshotFields.BucketId, snapshot.BucketId},
                        {MongoSnapshotFields.StreamId, snapshot.StreamId},
                        {MongoSnapshotFields.StreamRevision, snapshot.StreamRevision}
                    }
                },
                { MongoSnapshotFields.Payload, BsonDocumentWrapper.Create(serializer.Serialize(snapshot.Payload)) }
            };
        }

        /// <summary>
        /// Converts a <see cref="BsonDocument"/> to a <see cref="ISnapshot"/>.
        /// </summary>
        public static Snapshot ToSnapshot(this BsonDocument doc, IDocumentSerializer serializer)
        {
            BsonDocument id = doc[MongoSnapshotFields.Id].AsBsonDocument;
            string bucketId = id[MongoSnapshotFields.BucketId].AsString;
            string streamId = id[MongoSnapshotFields.StreamId].AsString;
            int streamRevision = id[MongoSnapshotFields.StreamRevision].AsInt32;
            BsonValue bsonPayload = doc[MongoSnapshotFields.Payload];

            object payload;
            switch (bsonPayload.BsonType)
            {
                case BsonType.Binary:
                    payload = serializer.Deserialize<object>(bsonPayload.AsByteArray)!;
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
                builder.Eq(MongoSnapshotFields.FullQualifiedBucketId, bucketId),
                builder.Eq(MongoSnapshotFields.FullQualifiedStreamId, streamId),
                builder.Lte(MongoSnapshotFields.FullQualifiedStreamRevision, maxRevision)
            );
        }
    }
}