using MongoDB.Bson.Serialization;

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
        public static Dictionary<Tkey, Tvalue> AsDictionary<Tkey, Tvalue>(this BsonValue bsonValue)
        {
            using (BsonReader reader = new JsonReader(bsonValue.ToJson()))
            {
                var dictionarySerializer = new DictionaryInterfaceImplementerSerializer<Dictionary<Tkey, Tvalue>>();
                object result = dictionarySerializer.Deserialize(
                    BsonDeserializationContext.CreateRoot(reader, b => { }),
                    new BsonDeserializationArgs()
                    {
                        NominalType = typeof(Dictionary<Tkey, Tvalue>)
                    }
                );
                return (Dictionary<Tkey, Tvalue>)result;
            }
        }

        public static BsonDocument ToMongoCommit(this CommitAttempt commit, LongCheckpoint checkpoint, IDocumentSerializer serializer)
        {
            int streamRevision = commit.StreamRevision - (commit.Events.Count - 1);
            int streamRevisionStart = streamRevision;
            IEnumerable<BsonDocument> events = commit
                .Events
                .Select(e =>
                    new BsonDocument
                    {
                        {MongoCommitFields.StreamRevision, streamRevision++},
                        {MongoCommitFields.Payload, BsonDocumentWrapper.Create(serializer.Serialize(e))}
                    });
            return new BsonDocument
            {
                {MongoCommitFields.CheckpointNumber, checkpoint.LongValue},
                {MongoCommitFields.CommitId, commit.CommitId},
                {MongoCommitFields.CommitStamp, commit.CommitStamp},
                {MongoCommitFields.Headers, BsonDocumentWrapper.Create(commit.Headers)},
                {MongoCommitFields.Events, new BsonArray(events)},
                {MongoCommitFields.Dispatched, false},
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

            string bucketId = doc[MongoCommitFields.BucketId].AsString;
            string streamId = doc[MongoCommitFields.StreamId].AsString;
            int commitSequence = doc[MongoCommitFields.CommitSequence].AsInt32;

            List<EventMessage> events = doc[MongoCommitFields.Events]
                .AsBsonArray
                .Select(e => e.AsBsonDocument[MongoCommitFields.Payload].IsBsonDocument
                    ? BsonSerializer.Deserialize<EventMessage>(e.AsBsonDocument[MongoCommitFields.Payload].ToBsonDocument())
                    : serializer.Deserialize<EventMessage>(e.AsBsonDocument[MongoCommitFields.Payload].AsByteArray))
                .ToList();
            //int streamRevision = doc[MongoCommitFields.Events].AsBsonArray.Last().AsBsonDocument[MongoCommitFields.StreamRevision].AsInt32;
            int streamRevision = doc[MongoCommitFields.StreamRevisionTo].AsInt32;
            return new Commit(bucketId,
                streamId,
                streamRevision,
                doc[MongoCommitFields.CommitId].AsGuid,
                commitSequence,
                doc[MongoCommitFields.CommitStamp].ToUniversalTime(),
                new LongCheckpoint(doc[MongoCommitFields.CheckpointNumber].ToInt64()).Value,
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

            return
                builder.And(
                    builder.Gt(MongoShapshotFields.Id,
                        builder.And(
                            builder.Eq(MongoShapshotFields.BucketId, bucketId),
                            builder.Eq(MongoShapshotFields.StreamId, streamId),
                            builder.Eq(MongoShapshotFields.StreamRevision, BsonNull.Value)
                        ).ToBsonDocument()),
                    builder.Lte(MongoShapshotFields.Id,
                        builder.And(
                            builder.Eq(MongoShapshotFields.BucketId, bucketId),
                            builder.Eq(MongoShapshotFields.StreamId, streamId),
                            builder.Eq(MongoShapshotFields.StreamRevision, maxRevision)
                         ).ToBsonDocument())
                    );
        }
    }
}