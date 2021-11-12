using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace NEventStore.Persistence.MongoDB
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using global::MongoDB.Bson;
    using global::MongoDB.Bson.IO;
    using global::MongoDB.Bson.Serialization.Options;
    using global::MongoDB.Bson.Serialization.Serializers;
    using global::MongoDB.Driver;
    using NEventStore.Serialization;
    using BsonSerializer = global::MongoDB.Bson.Serialization.BsonSerializer;

    public static class ExtensionMethods
    {
        [Obsolete("Original code, not used anymore, replaced by the new configurable version")]
        public static Dictionary<Tkey, Tvalue> AsDictionary<Tkey, Tvalue>(this BsonValue bsonValue)
        {
            using (BsonReader reader = new JsonReader(bsonValue.ToJson()))
            {
                var dictionarySerializer = new DictionaryInterfaceImplementerSerializer<Dictionary<Tkey, Tvalue>>();
                object result = dictionarySerializer.Deserialize(
                    BsonDeserializationContext.CreateRoot(reader, _ => { }),
                    new BsonDeserializationArgs()
                    {
                        NominalType = typeof(Dictionary<Tkey, Tvalue>)
                    }
                );
                return (Dictionary<Tkey, Tvalue>)result;
            }
        }

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

        [Obsolete("Original code, not used anymore, replaced by the new configurable version")]
        public static BsonDocument ToMongoCommit_original(this CommitAttempt commit, Int64 checkpoint, IDocumentSerializer serializer)
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

            //var dictionarySerialize = new DictionaryInterfaceImplementerSerializer<Dictionary<string, object>>(DictionaryRepresentation.ArrayOfArrays);
            //var dicSer = BsonSerializer.LookupSerializer<Dictionary<string, object>>();

            return new BsonDocument
            {
                {MongoCommitFields.CheckpointNumber, checkpoint},
                {MongoCommitFields.CommitId, commit.CommitId},
                {MongoCommitFields.CommitStamp, commit.CommitStamp},
                {MongoCommitFields.Headers, BsonDocumentWrapper.Create(commit.Headers)},
                {MongoCommitFields.Events, new BsonArray(events)},
                {MongoCommitFields.StreamRevisionFrom, streamRevisionStart},
                {MongoCommitFields.StreamRevisionTo, streamRevision - 1},
                {MongoCommitFields.BucketId, commit.BucketId},
                {MongoCommitFields.StreamId, commit.StreamId},
                {MongoCommitFields.CommitSequence, commit.CommitSequence}
            };
        }

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
                    BsonValue payload = e.AsBsonDocument[MongoCommitFields.Payload];
                    return payload.IsBsonDocument
                           ? BsonSerializer.Deserialize<EventMessage>(payload.ToBsonDocument())
                           : serializer.Deserialize<EventMessage>(payload.AsByteArray); // ByteStreamDocumentSerializer ?!?! doesn't work this way!
                }).ToArray());
        }

        [Obsolete("Original code, not used anymore, replaced by the new configurable version")]
        public static ICommit ToCommit_original(this BsonDocument doc, IDocumentSerializer serializer)
        {
            if (doc == null)
            {
                return null;
            }

            string bucketId = doc[MongoCommitFields.BucketId].AsString;
            string streamId = doc[MongoCommitFields.StreamId].AsString;
            int commitSequence = doc[MongoCommitFields.CommitSequence].AsInt32;

            var events = doc[MongoCommitFields.Events]
                .AsBsonArray
                .Select(e =>
                {
                    BsonValue payload = e.AsBsonDocument[MongoCommitFields.Payload];
                    return payload.IsBsonDocument
                           ? BsonSerializer.Deserialize<EventMessage>(payload.ToBsonDocument())
                           : serializer.Deserialize<EventMessage>(payload.AsByteArray);
                })
                .ToArray();

            //int streamRevision = doc[MongoCommitFields.Events].AsBsonArray.Last().AsBsonDocument[MongoCommitFields.StreamRevision].AsInt32;
            int streamRevision = doc[MongoCommitFields.StreamRevisionTo].AsInt32;
            return new Commit(bucketId,
                streamId,
                streamRevision,
                doc[MongoCommitFields.CommitId].AsGuid,
                commitSequence,
                doc[MongoCommitFields.CommitStamp].ToUniversalTime(),
                doc[MongoCommitFields.CheckpointNumber].ToInt64(),
                doc[MongoCommitFields.Headers].AsDictionary<string, object>(),
                events);
        }

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

        public static StreamHead ToStreamHead(this BsonDocument doc)
        {
            BsonDocument id = doc[MongoStreamHeadFields.Id].AsBsonDocument;
            string bucketId = id[MongoStreamHeadFields.BucketId].AsString;
            string streamId = id[MongoStreamHeadFields.StreamId].AsString;
            return new StreamHead(bucketId, streamId, doc[MongoStreamHeadFields.HeadRevision].AsInt32, doc[MongoStreamHeadFields.SnapshotRevision].AsInt32);
        }

        public static FilterDefinition<BsonDocument> ToMongoCommitIdQuery(this CommitAttempt commit)
        {
            var builder = Builders<BsonDocument>.Filter;
            return builder.And(
                builder.Eq(MongoCommitFields.BucketId, commit.BucketId),
                builder.Eq(MongoCommitFields.StreamId, commit.StreamId),
                builder.Eq(MongoCommitFields.CommitId, commit.CommitId)
            );
        }

        public static FilterDefinition<BsonDocument> ToMongoCommitIdQuery(this ICommit commit)
        {
            var builder = Builders<BsonDocument>.Filter;
            return builder.And(
                builder.Eq(MongoCommitFields.BucketId, commit.BucketId),
                builder.Eq(MongoCommitFields.StreamId, commit.StreamId),
                builder.Eq(MongoCommitFields.CommitId, commit.CommitId)
            );
        }

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

    // let's ignore the extra elements, the 'Dispatched' field and the dispatched concept have been dropped in NEventStore 6
    [BsonIgnoreExtraElements]
    public sealed class MongoCommit
    {
        [BsonId]
        [BsonElement(MongoCommitFields.CheckpointNumber)]
        public long CheckpointNumber { get; set; }

        [BsonElement(MongoCommitFields.CommitId)]
        public Guid CommitId { get; set; }

        [BsonElement(MongoCommitFields.CommitStamp)]
        public DateTime CommitStamp { get; set; }

        [BsonElement(MongoCommitFields.Headers)]
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfArrays)] // we can override this specifing a classmap OR implementing an IBsonSerializer and an IBsonSerializationProvider 
        public IDictionary<string, object> Headers { get; set; }

        // multiple evaluations using linq can be dangerous, maybe it's better have a plain array to avoid bugs
        [BsonElement(MongoCommitFields.Events)]
        public BsonArray Events { get; set; }

        [BsonElement(MongoCommitFields.StreamRevisionFrom)]
        public int StreamRevisionFrom { get; set; }

        [BsonElement(MongoCommitFields.StreamRevisionTo)]
        public int StreamRevisionTo { get; set; }

        [BsonElement(MongoCommitFields.BucketId)]
        public string BucketId { get; set; }

        [BsonElement(MongoCommitFields.StreamId)]
        public string StreamId { get; set; }

        [BsonElement(MongoCommitFields.CommitSequence)]
        public int CommitSequence { get; set; }
    }
}