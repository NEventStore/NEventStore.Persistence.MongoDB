using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public abstract class Issue_87_recycle_collision : SpecificationBase
    {
        private MongoPersistenceEngine? _engine;
        private IMongoDatabase? _database;
        private Exception? _error;
        private const string Stream = "shared-stream";
        protected virtual bool Async => false;
        protected virtual bool Recreate => false;
        private string SecondBucket => Recreate ? "bucket-a" : "bucket-b";

        protected override async Task ContextAsync()
        {
            // Initialize the same legacy serializers used by the acceptance suite.
            _ = new AcceptanceTestMongoPersistenceFactory();
            var options = new AcknowledgedMetadataOptions { PersistStreamHeadsOnBackgroundThread = false };
            var url = new MongoUrlBuilder(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
            {
                DatabaseName = $"issue87-{Guid.NewGuid():N}"
            };
            _database = options.ConnectToDatabase(url.ToString());
            _engine = new MongoPersistenceEngine(_database, new DocumentObjectSerializer(), options);
            _engine.Initialize();
            await Seed("bucket-a");
            await Delete("bucket-a");
            await Seed(SecondBucket);
        }

        private async Task Seed(string bucket)
        {
            if (Async)
            {
                await _engine!.CommitManyAsync(2, Stream, bucket);
                (await _engine!.AddSnapshotAsync(new Snapshot(bucket, Stream, 2, "snapshot"), CancellationToken.None)).Should().BeTrue();
            }
            else
            {
                _engine!.CommitMany(2, Stream, bucket);
                _engine!.AddSnapshot(new Snapshot(bucket, Stream, 2, "snapshot")).Should().BeTrue();
            }
        }

        private Task Delete(string bucket)
        {
            if (Async)
                return _engine!.DeleteStreamAsync(bucket, Stream, CancellationToken.None);
            _engine!.DeleteStream(bucket, Stream);
            return Task.CompletedTask;
        }

        protected override async Task BecauseAsync()
        {
            try { await Delete(SecondBucket); }
            catch (Exception error) { _error = error; }
        }

        [Fact]
        public void Independent_stream_lifetimes_can_be_deleted_without_unique_key_collisions()
        {
            _error.Should().BeNull();
            _engine!.GetDeletedCommits().Should().HaveCount(4)
                .And.OnlyContain(commit => commit.StreamId == Stream && commit.BucketId == MongoSystemBuckets.RecycleBin);
            _engine.GetFrom(0).Should().BeEmpty();
            _engine.GetDeletedCommits().Select(commit => commit.CheckpointToken).Should().Equal(1L, 2L, 3L, 4L);
        }

        [Fact]
        public void Successful_recycling_removes_live_commits_heads_and_snapshots()
        {
            Count("Commits", MongoCommitFields.BucketId, SecondBucket).Should().Be(0);
            Count("Commits", MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin).Should().Be(4);
            Count("Streams", MongoStreamHeadFields.FullQualifiedBucketId, SecondBucket).Should().Be(0);
            Count("Snapshots", MongoSnapshotFields.FullQualifiedBucketId, SecondBucket).Should().Be(0);
        }

        private long Count(string collection, string field, string value) =>
            _database!.GetCollection<BsonDocument>(collection).CountDocuments(new BsonDocument(field, value));

        protected override void Cleanup()
        {
            _engine?.Dispose();
            if (_database != null)
                _database.Client.DropDatabase(_database.DatabaseNamespace.DatabaseName);
        }

        private sealed class AcknowledgedMetadataOptions : MongoPersistenceOptions
        {
            public override MongoCollectionSettings GetStreamSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
            public override MongoCollectionSettings GetSnapshotSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
        }
    }

    public class Issue_87_when_same_stream_is_deleted_in_different_buckets : Issue_87_recycle_collision { }
    public class Issue_87_when_same_stream_is_deleted_in_different_buckets_async : Issue_87_recycle_collision
    {
        protected override bool Async => true;
    }
    public class Issue_87_when_a_deleted_stream_is_recreated_and_deleted : Issue_87_recycle_collision
    {
        protected override bool Recreate => true;
    }
    public class Issue_87_when_a_deleted_stream_is_recreated_and_deleted_async : Issue_87_recycle_collision
    {
        protected override bool Recreate => true;
        protected override bool Async => true;
    }
}
