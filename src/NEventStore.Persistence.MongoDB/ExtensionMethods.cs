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
					BsonDeserializationContext.CreateRoot(reader, b => { }),
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
            if (serializer is DocumentObjectSerializer)
                return ToMongoCommit(commit, checkpoint);

			int streamRevision = commit.StreamRevision - (commit.Events.Count - 1);
			int streamRevisionStart = streamRevision;

			IEnumerable<BsonDocument> events = commit
				.Events
				.Select(e =>
					new BsonDocument
					{
						{MongoCommitFields.StreamRevision, streamRevision++},
						{MongoCommitFields.Payload, BsonDocumentWrapper.Create(typeof(EventMessage), serializer.Serialize(e))}
					}).ToList();

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

        public static BsonDocument ToMongoCommit(this CommitAttempt commit, Int64 checkpoint)
        {
            int streamRevision = commit.StreamRevision - (commit.Events.Count - 1);
            int streamRevisionStart = streamRevision;

            var mc = new PlainMongoCommit()
            {
                CheckpointToken = checkpoint,
                CommitId = commit.CommitId,
                CommitStamp = commit.CommitStamp,
                Headers = commit.Headers,
                Events = commit.Events.Select(e => new PlainMongoCommit.PlainEvent() {
                    StreamRevision = streamRevision++,
                    Payload = e,
                }).ToList(),
                StreamRevisionFrom = streamRevisionStart,
                StreamRevisionTo = streamRevision - 1,
                BucketId = commit.BucketId,
                StreamId = commit.StreamId,
                CommitSequence = commit.CommitSequence
            };

            return mc.ToBsonDocument();
        }

        public static BsonDocument ToEmptyCommit(this CommitAttempt commit, Int64 checkpoint, IDocumentSerializer serializer, String systemBucketName)
		{
			if (commit == null) throw new ArgumentNullException("commit");
            if (serializer is DocumentObjectSerializer)
                return ToEmptyCommit(commit, checkpoint, systemBucketName);

            if (String.IsNullOrEmpty(systemBucketName)) throw new ArgumentNullException("systemBucketName");
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
				Headers = new Dictionary<String, Object>(),
				Events = new BsonArray(new object[] { }),
				StreamRevisionFrom = 0,
				StreamRevisionTo = 0,
				BucketId = systemBucketName,
				StreamId = systemBucketName + ".empty." + checkpoint,
				CommitSequence = 1
			};

			return mc.ToBsonDocument();
		}

        public static BsonDocument ToEmptyCommit(this CommitAttempt commit, Int64 checkpoint, String systemBucketName)
        {
            if (commit == null) throw new ArgumentNullException("commit");
            if (String.IsNullOrEmpty(systemBucketName)) throw new ArgumentNullException("systemBucketName");

            var mc = new PlainMongoCommit
            {
                CheckpointToken = checkpoint,
                CommitId = commit.CommitId,
                CommitStamp = commit.CommitStamp,
                Headers = new Dictionary<String, Object>(),
                Events = new List<PlainMongoCommit.PlainEvent>(),
                StreamRevisionFrom = 0,
                StreamRevisionTo = 0,
                BucketId = systemBucketName,
                StreamId = systemBucketName + ".empty." + checkpoint,
                CommitSequence = 1
            };

            return mc.ToBsonDocument();
        }


     
		public static ICommit ToCommit(this BsonDocument doc, IDocumentSerializer serializer)
		{
			if (doc == null)
			{
				return null;
			}
            if (serializer is DocumentObjectSerializer)
                return ToCommit(doc);

			var mc = BsonSerializer.Deserialize<MongoCommit>(doc);

			return new Commit(mc.BucketId,
				mc.StreamId,
				mc.StreamRevisionTo,
				mc.CommitId,
				mc.CommitSequence,
				mc.CommitStamp,
				mc.CheckpointNumber,
				mc.Headers,
				mc.Events.Select(e => e.AsBsonDocument[MongoCommitFields.Payload].IsBsonDocument
					? BsonSerializer.Deserialize<EventMessage>(e.AsBsonDocument[MongoCommitFields.Payload].ToBsonDocument())
					: serializer.Deserialize<EventMessage>(e.AsBsonDocument[MongoCommitFields.Payload].AsByteArray))); // ByteStreamDocumentSerializer ?!?! doesn't work this way!
		}

        public static ICommit ToCommit(this BsonDocument doc)
        {
            if (doc == null)
            {
                return null;
            }

            return BsonSerializer.Deserialize<PlainMongoCommit>(doc);
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
		public long CheckpointNumber { get; set; }

		public Guid CommitId { get; set; }
		public DateTime CommitStamp { get; set; }

		[BsonDictionaryOptions(DictionaryRepresentation.ArrayOfArrays)] // we can override this specifing a classmap OR implementing an IBsonSerializer and an IBsonSerializationProvider 
		public IDictionary<string, object> Headers { get; set; }
		// multiple evaluations using linq can be dangerous, maybe it's better have a plain array to avoid bugs
		public BsonArray Events { get; set; }
		public int StreamRevisionFrom { get; set; }
		public int StreamRevisionTo { get; set; }
		public string BucketId { get; set; }
		public string StreamId { get; set; }
		public int CommitSequence { get; set; }
	}

    // let's ignore the extra elements, the 'Dispatched' field and the dispatched concept have been dropped in NEventStore 6
    [BsonIgnoreExtraElements]
    public sealed class PlainMongoCommit : ICommit
    {
        [BsonId]
        public long CheckpointToken { get; set; }

        public Guid CommitId { get; set; }
        public DateTime CommitStamp { get; set; }

        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfArrays)] // we can override this specifing a classmap OR implementing an IBsonSerializer and an IBsonSerializationProvider 
        public IDictionary<string, object> Headers { get; set; }
        // multiple evaluations using linq can be dangerous, maybe it's better have a plain array to avoid bugs
        public ICollection<PlainEvent> Events { get; set; }
        public int StreamRevisionFrom { get; set; }
        public int StreamRevisionTo { get; set; }

        [BsonIgnore]
        public int StreamRevision { get { return StreamRevisionTo; } }

        public string BucketId { get; set; }
        public string StreamId { get; set; }
        public int CommitSequence { get; set; }

        private List<EventMessage> _plainEvents;
        ICollection<EventMessage> ICommit.Events
        {
            get
            {
                if (Events == null) return new List<EventMessage>();
                return _plainEvents ?? (_plainEvents = Events.Select(e => e.Payload).ToList());
            }
        }

        public class PlainEvent
        {
            public Int32 StreamRevision { get; set; }

            public EventMessage Payload { get; set; }
        }
    }
}