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
			int streamRevision = commit.StreamRevision - (commit.Events.Count - 1);
			int streamRevisionStart = streamRevision;
			MongoCommitEvent[] events = commit
				.Events
				.Select(e =>
					new MongoCommitEvent
					{
						StreamRevision = streamRevision++,
						Payload = BsonDocumentWrapper.Create(serializer.Serialize(e))
					}).ToArray();

			var mc = new MongoCommit
			{
				CheckpointNumber = checkpoint,
				CommitId = commit.CommitId,
				CommitStamp = commit.CommitStamp,
				Headers = commit.Headers, // as Dictionary<string, object>,	// this is bad, we are assuming it is a dictionary!
				Events = events,
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
			if (String.IsNullOrEmpty(systemBucketName)) throw new ArgumentNullException("systemBucketName");
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

			var mc = new MongoCommit
			{
				CheckpointNumber = checkpoint,
				CommitId = commit.CommitId,
				CommitStamp = commit.CommitStamp,
				Headers = new Dictionary<String, Object>(),
				Events = new MongoCommitEvent[] { },
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
						{MongoCommitFields.Payload, BsonDocumentWrapper.Create(serializer.Serialize(e))}
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

			var mc = BsonSerializer.Deserialize<MongoCommit>(doc);

			return new Commit(mc.BucketId,
				mc.StreamId,
				mc.StreamRevisionTo,
				mc.CommitId,
				mc.CommitSequence,
				mc.CommitStamp,
				mc.CheckpointNumber,
				mc.Headers,
				mc.Events.Select(e => e.Payload.IsBsonDocument
					? BsonSerializer.Deserialize<EventMessage>(e.Payload)
					: serializer.Deserialize<EventMessage>(e.Payload.AsByteArray))); // ByteStreamDocumentSerializer ?!?! doesn't work this way!
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

	public class MongoCommit
	{
		[BsonId]
		public long CheckpointNumber { get; set; }

		public Guid CommitId { get; set; }
		public DateTime CommitStamp { get; set; }

		[BsonDictionaryOptions(DictionaryRepresentation.ArrayOfArrays)] // we can override this specifing a classmap OR implementing an IBsonSerializer and an IBsonSerializationProvider 
		public IDictionary<string, object> Headers { get; set; }
		// multiple evaluations using linq can be dangerous, maybe it's better have a plain array to avoid bugs
		public IEnumerable<MongoCommitEvent> Events { get; set; }
		public int StreamRevisionFrom { get; set; }
		public int StreamRevisionTo { get; set; }
		public string BucketId { get; set; }
		public string StreamId { get; set; }
		public int CommitSequence { get; set; }
	}

	// we can fully qualify this class if we get rid of using IDocumentSerializer or we can revert to having a BsonArray and do
	// everything by hand once again.
	public class MongoCommitEvent
	{
		public int StreamRevision { get; set; }

		// the Payload Here can be an EventMessage to keep the serialization even simpler.
		public BsonDocument Payload { get; set; }
	}
}