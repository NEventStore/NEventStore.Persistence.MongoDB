using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Persistence.MongoDB.Support;
using NEventStore.Serialization;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public abstract class Issue_87_seeded_stream : SpecificationBase
    {
        protected const string Bucket = "bucket";
        protected const string Stream = "stream";
        protected IMongoDatabase Database = null!;
        protected MongoPersistenceEngine Engine = null!;
        protected MongoPersistenceOptions Options = null!;
        protected virtual bool Async => false;

        protected override void Context()
        {
            _ = new AcceptanceTestMongoPersistenceFactory();
            Options = new AcknowledgedMetadataOptions { PersistStreamHeadsOnBackgroundThread = false };
            var url = new MongoUrlBuilder(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
            {
                DatabaseName = $"issue87-{Guid.NewGuid():N}"
            };
            Database = Options.ConnectToDatabase(url.ToString());
            Engine = new MongoPersistenceEngine(Database, new DocumentObjectSerializer(), Options);
            Engine.Initialize();
            Engine.CommitMany(2, Stream, Bucket);
            Engine.AddSnapshot(new Snapshot(Bucket, Stream, 2, "snapshot")).Should().BeTrue();
        }

        protected long Count(string collection, string field, string value) =>
            Database.GetCollection<BsonDocument>(collection).CountDocuments(new BsonDocument(field, value));

        protected override void Cleanup()
        {
            Engine?.Dispose();
            if (Database != null)
                Database.Client.DropDatabase(Database.DatabaseNamespace.DatabaseName);
        }

        private sealed class AcknowledgedMetadataOptions : MongoPersistenceOptions
        {
            public override MongoCollectionSettings GetStreamSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
            public override MongoCollectionSettings GetSnapshotSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
        }
    }

    public class Issue_87_when_recycling_update_fails : Issue_87_seeded_stream
    {
        private Exception? _error;

        protected override void Context()
        {
            base.Context();
            Database.RunCommand<BsonDocument>(new BsonDocument
            {
                { "collMod", "Commits" },
                { "validator", new BsonDocument(MongoCommitFields.BucketId,
                    new BsonDocument("$ne", MongoSystemBuckets.RecycleBin)) },
                { "validationLevel", "strict" },
                { "validationAction", "error" }
            });
        }

        protected override async Task BecauseAsync()
        {
            try
            {
                if (Async)
                    await Engine.DeleteStreamAsync(Bucket, Stream, CancellationToken.None);
                else
                    Engine.DeleteStream(Bucket, Stream);
            }
            catch (Exception error) { _error = error; }
        }

        [Fact]
        public void The_storage_failure_is_propagated()
        {
            var cause = _error.Should().BeOfType<StorageException>().Which.InnerException;
            cause.Should().BeOfType<MongoWriteException>().Which.WriteError.Code.Should().Be(121);
        }

        [Fact]
        public void Live_commits_remain_unchanged()
        {
            Engine.GetFrom(Bucket, Stream, 1, 4).Select(commit => commit.CheckpointToken).Should().Equal(1L, 2L);
            Count("Commits", MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin).Should().Be(0);
        }

        [Fact]
        public void The_live_head_is_retained() =>
            Count("Streams", MongoStreamHeadFields.FullQualifiedBucketId, Bucket).Should().Be(1);

        [Fact]
        public void The_live_snapshot_is_retained() =>
            Count("Snapshots", MongoSnapshotFields.FullQualifiedBucketId, Bucket).Should().Be(1);

    }

    public class Issue_87_when_recycling_update_fails_async : Issue_87_when_recycling_update_fails
    {
        protected override bool Async => true;
    }

    public class Issue_87_when_reading_legacy_recycled_commits : Issue_87_seeded_stream
    {
        protected override void Because()
        {
            // Seed the legacy on-disk format directly, independently of the current delete implementation.
            Database.GetCollection<BsonDocument>("Commits").UpdateMany(
                Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, Bucket),
                Builders<BsonDocument>.Update.Set(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin));
        }

        [Fact]
        public async Task Original_stream_identity_and_checkpoints_are_preserved()
        {
            IEnumerable<ICommit> commits;
            if (Async)
            {
                var observer = new CommitStreamObserver();
                await Engine.GetDeletedCommitsAsync(observer, CancellationToken.None);
                commits = observer.Commits;
            }
            else
                commits = Engine.GetDeletedCommits();
            commits.Should().OnlyContain(commit => commit.StreamId == Stream && commit.BucketId == MongoSystemBuckets.RecycleBin);
            commits.Select(commit => commit.CheckpointToken).Should().Equal(1L, 2L);
        }

        [Fact]
        public async Task Recycle_bucket_can_be_read_by_original_stream_and_revision_range()
        {
            IEnumerable<ICommit> commits;
            if (Async)
            {
                var observer = new CommitStreamObserver();
                await Engine.GetFromAsync(MongoSystemBuckets.RecycleBin, Stream, 3, 3, observer, CancellationToken.None);
                commits = observer.Commits;
            }
            else
                commits = Engine.GetFrom(MongoSystemBuckets.RecycleBin, Stream, 3, 3);
            commits.Select(commit => commit.CheckpointToken).Should().Equal(2L);
        }
    }

    public class Issue_87_when_reading_legacy_recycled_commits_async : Issue_87_when_reading_legacy_recycled_commits
    {
        protected override bool Async => true;
    }

    public class Issue_87_when_cleaning_legacy_recycled_commits : Issue_87_seeded_stream
    {
        protected override async Task BecauseAsync()
        {
            Database.GetCollection<BsonDocument>("Commits").UpdateMany(
                Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, Bucket),
                Builders<BsonDocument>.Update.Set(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin));
            Engine.CommitMany(1, "live-stream", "live-bucket");
            if (Async)
                await Engine.EmptyRecycleBinAsync(CancellationToken.None);
            else
                Engine.EmptyRecycleBin();
        }

        [Fact]
        public void Cleanup_removes_legacy_recycled_documents_without_touching_live_commits()
        {
            Engine.GetDeletedCommits().Should().BeEmpty();
            Engine.GetFrom(0).Select(commit => commit.CheckpointToken).Should().Equal(3L);
        }
    }

    public class Issue_87_when_cleaning_legacy_recycled_commits_async : Issue_87_when_cleaning_legacy_recycled_commits
    {
        protected override bool Async => true;
    }
}
