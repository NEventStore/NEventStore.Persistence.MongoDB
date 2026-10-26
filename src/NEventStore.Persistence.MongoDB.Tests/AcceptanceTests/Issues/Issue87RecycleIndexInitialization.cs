using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public class Issue_87_when_initializing_an_existing_recycle_store : SpecificationBase
    {
        private IMongoDatabase _database = null!;
        private IMongoCollection<BsonDocument> _commits = null!;
        private readonly MongoPersistenceOptions _options = new();
        private List<BsonDocument> _originalIndexes = null!;
        private List<BsonDocument> _originalDocuments = null!;
        private List<BsonDocument> _firstIndexes = null!;
        private List<BsonDocument> _repeatedIndexes = null!;

        protected override void Context()
        {
            var url = new MongoUrlBuilder(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
            {
                DatabaseName = $"issue87-index-upgrade-{Guid.NewGuid():N}"
            };
            _database = _options.ConnectToDatabase(url.ToString());
            _commits = _database.GetCollection<BsonDocument>("Commits");
            using (var engine = NewEngine())
                engine.Initialize();

            // Model an existing installation without changing any of its old indexes.
            if (ReadIndexes().Any(index => index["name"] == MongoCommitIndexes.GetFromRecycleBin))
                _commits.Indexes.DropOne(MongoCommitIndexes.GetFromRecycleBin);
            _commits.InsertMany(new[]
            {
                Document(1, MongoSystemBuckets.RecycleBin, "deleted-attempt-1", "original-stream"),
                Document(2, MongoSystemBuckets.RecycleBin, "deleted-attempt-2", "original-stream"),
                Document(3, MongoSystemBuckets.RecycleBin, "legacy-stream", null),
                Document(4, "live-bucket", "live-stream", "original-stream")
            });
            _originalIndexes = ReadIndexes();
            _originalDocuments = ReadDocuments();
        }

        protected override void Because()
        {
            using (var engine = NewEngine())
            {
                engine.Initialize();
                _firstIndexes = ReadIndexes();
                engine.Initialize();
            }
            // A new process/engine must also tolerate the already-created index.
            using (var engine = NewEngine())
                engine.Initialize();
            _repeatedIndexes = ReadIndexes();
        }

        [Fact]
        public void Upgrade_adds_one_nonunique_recycle_only_index_to_populated_storage()
        {
            var added = _firstIndexes.Where(index => !_originalIndexes.Any(original => original["name"] == index["name"]))
                .Should().ContainSingle().Which;
            added["name"].Should().Be(new BsonString(MongoCommitIndexes.GetFromRecycleBin));
            added["key"].Should().Be(new BsonDocument
            {
                { MongoCommitFields.BucketId, 1 },
                { MongoCommitFields.OriginalStreamId, 1 },
                { MongoCommitFields.StreamRevisionFrom, 1 },
                { MongoCommitFields.StreamRevisionTo, 1 }
            });
            added.GetValue("unique", false).AsBoolean.Should().BeFalse(
                "original identities and revision ranges repeat across deleted lifetimes");
            added["partialFilterExpression"].Should().Be(
                new BsonDocument(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin),
                "live commits must not occupy the recycle lookup index, even if optional metadata is present");
        }

        [Fact]
        public void Existing_unique_indexes_and_documents_remain_unchanged()
        {
            foreach (var original in _originalIndexes)
                _repeatedIndexes.Single(index => index["name"] == original["name"]).Equals(original).Should().BeTrue();
            ReadDocuments().Should().Equal(_originalDocuments, "index migration must not rewrite legacy or new identities");
        }

        [Fact]
        public void Initialization_is_idempotent_for_the_same_and_new_engine_instances() =>
            _repeatedIndexes.Should().Equal(_firstIndexes);

        private MongoPersistenceEngine NewEngine() => new(_database, new DocumentObjectSerializer(), _options);

        private List<BsonDocument> ReadIndexes() => _commits.Indexes.List().ToList()
            .OrderBy(index => index["name"].AsString, StringComparer.Ordinal).ToList();

        private List<BsonDocument> ReadDocuments() => _commits.Find(new BsonDocument())
            .Sort(new BsonDocument(MongoCommitFields.CheckpointNumber, 1)).ToList();

        private static BsonDocument Document(long checkpoint, string bucket, string stream, string? originalStream)
        {
            var document = new BsonDocument
            {
                { MongoCommitFields.CheckpointNumber, checkpoint },
                { MongoCommitFields.BucketId, bucket }, { MongoCommitFields.StreamId, stream },
                { MongoCommitFields.StreamRevisionFrom, 1 }, { MongoCommitFields.StreamRevisionTo, 2 },
                { MongoCommitFields.CommitSequence, 1 }, { MongoCommitFields.CommitId, $"commit-{checkpoint}" }
            };
            if (originalStream != null)
                document[MongoCommitFields.OriginalStreamId] = originalStream;
            return document;
        }

        protected override void Cleanup()
        {
            if (_database != null)
                _database.Client.DropDatabase(_database.DatabaseNamespace.DatabaseName);
        }
    }
}
