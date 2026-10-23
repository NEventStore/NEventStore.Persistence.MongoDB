using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Bson.Serialization;
using global::MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Persistence.MongoDB.Support;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public class Issue_87_when_reading_mixed_recycled_identities : Issue_87_seeded_stream
    {
        protected override async Task BecauseAsync()
        {
            Database.GetCollection<BsonDocument>("Commits").UpdateMany(
                new BsonDocument(MongoCommitFields.BucketId, Bucket),
                Builders<BsonDocument>.Update.Set(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin));
            Engine.CommitMany(2, Stream, "another-bucket");
            if (Async)
                await Engine.DeleteStreamAsync("another-bucket", Stream, CancellationToken.None);
            else
                Engine.DeleteStream("another-bucket", Stream);
            Engine.CommitMany(1, "live-stream", "live-bucket");
            // Optional metadata must never override identity on a live document.
            Database.GetCollection<BsonDocument>("Commits").UpdateMany(
                new BsonDocument(MongoCommitFields.BucketId, "live-bucket"),
                Builders<BsonDocument>.Update.Set(MongoCommitFields.OriginalStreamId, Stream));
        }

        [Fact]
        public async Task Original_identity_reads_combine_new_and_legacy_documents()
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
            commits.Select(c => c.CheckpointToken).Should().BeEquivalentTo(new[] { 2L, 4L });
            commits.Should().OnlyContain(c => c.StreamId == Stream && c.BucketId == MongoSystemBuckets.RecycleBin);
        }

        [Fact]
        public async Task Checkpoint_and_deleted_readers_preserve_original_identity()
        {
            var commits = Engine.GetDeletedCommits().ToArray();
            commits.Select(c => c.CheckpointToken).Should().Equal(1L, 2L, 3L, 4L);
            commits.Should().OnlyContain(c => c.StreamId == Stream && c.BucketId == MongoSystemBuckets.RecycleBin);
            Engine.GetFrom(MongoSystemBuckets.RecycleBin, 0).Select(c => c.StreamId).Should().Equal(Enumerable.Repeat(Stream, 4));
            Engine.GetFromTo(MongoSystemBuckets.RecycleBin, 1L, 3L).Select(c => c.StreamId).Should().Equal(Stream, Stream);
            var observer = new CommitStreamObserver();
            await Engine.GetFromAsync(MongoSystemBuckets.RecycleBin, 0L, observer, CancellationToken.None);
            observer.Commits.Select(c => c.StreamId).Should().Equal(Enumerable.Repeat(Stream, 4));
            observer = new CommitStreamObserver();
            await Engine.GetFromToAsync(MongoSystemBuckets.RecycleBin, 1L, 3L, observer, CancellationToken.None);
            observer.Commits.Select(c => c.StreamId).Should().Equal(Stream, Stream);
            observer = new CommitStreamObserver();
            await Engine.GetDeletedCommitsAsync(observer, CancellationToken.None);
            observer.Commits.Select(c => c.StreamId).Should().Equal(Enumerable.Repeat(Stream, 4));
#pragma warning disable CS0618
            Engine.GetFrom(MongoSystemBuckets.RecycleBin, DateTime.UtcNow.AddDays(-1)).Select(c => c.StreamId).Should().Equal(Enumerable.Repeat(Stream, 4));
            Engine.GetFromTo(MongoSystemBuckets.RecycleBin, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)).Select(c => c.StreamId).Should().Equal(Enumerable.Repeat(Stream, 4));
#pragma warning restore CS0618
        }

        [Fact]
        public void Live_documents_do_not_use_optional_recycled_identity()
        {
            Engine.GetFrom("live-bucket", Stream, 0, int.MaxValue).Should().BeEmpty();
            Engine.GetFrom("live-bucket", "live-stream", 0, int.MaxValue).Single().StreamId.Should().Be("live-stream");
            Engine.GetFrom(0).Single().StreamId.Should().Be("live-stream");
        }

        [Fact]
        public void Raw_and_legacy_readers_expose_synthetic_identity_only_for_new_recycled_documents()
        {
            var documents = Database.GetCollection<BsonDocument>("Commits").Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToList();
            documents[0].Contains(MongoCommitFields.OriginalStreamId).Should().BeFalse();
            documents[2][MongoCommitFields.OriginalStreamId].AsString.Should().Be(Stream);
            documents[2][MongoCommitFields.OriginalBucketId].AsString.Should().Be("another-bucket");
            documents[2][MongoCommitFields.StreamId].Should().Be(documents[3][MongoCommitFields.StreamId]);
            BsonSerializer.Deserialize<MongoCommit>(documents[2]).StreamId.Should().NotBe(Stream);
        }
    }

    public class Issue_87_when_reading_mixed_recycled_identities_async : Issue_87_when_reading_mixed_recycled_identities
    {
        protected override bool Async => true;
    }

    public class Issue_87_when_cleaning_mixed_recycled_identities : Issue_87_seeded_stream
    {
        protected override async Task ContextAsync()
        {
            Database.GetCollection<BsonDocument>("Commits").UpdateMany(
                new BsonDocument(MongoCommitFields.BucketId, Bucket),
                Builders<BsonDocument>.Update.Set(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin));
            Engine.CommitMany(1, "live-stream", "live-bucket");
            Engine.CommitMany(2, Stream, "another-bucket");
            if (Async)
                await Engine.DeleteStreamAsync("another-bucket", Stream, CancellationToken.None);
            else
                Engine.DeleteStream("another-bucket", Stream);
        }

        protected override async Task BecauseAsync()
        {
            if (Async)
                await Engine.EmptyRecycleBinAsync(CancellationToken.None);
            else
                Engine.EmptyRecycleBin();
        }

        [Fact]
        public void Cleanup_removes_earlier_legacy_and_new_recycled_documents()
        {
            var documents = Database.GetCollection<BsonDocument>("Commits").Find(new BsonDocument())
                .Sort(new BsonDocument(MongoCommitFields.CheckpointNumber, 1)).ToList();
            documents.Select(document => document[MongoCommitFields.CheckpointNumber].AsInt64).Should().Equal(3L, 5L);
        }

        [Fact]
        public void Latest_recycled_commit_preserves_checkpoint_numbering_and_original_identity()
        {
            var commit = Engine.GetDeletedCommits().Should().ContainSingle().Which;
            commit.CheckpointToken.Should().Be(5L);
            commit.StreamId.Should().Be(Stream);
            commit.BucketId.Should().Be(MongoSystemBuckets.RecycleBin);
        }

        [Fact]
        public void Cleanup_leaves_the_live_commit_unchanged()
        {
            var commit = Engine.GetFrom(0).Should().ContainSingle().Which;
            commit.CheckpointToken.Should().Be(3L);
            commit.StreamId.Should().Be("live-stream");
            commit.BucketId.Should().Be("live-bucket");
        }
    }

    public class Issue_87_when_cleaning_mixed_recycled_identities_async : Issue_87_when_cleaning_mixed_recycled_identities
    {
        protected override bool Async => true;
    }

    public class Issue_87_when_retrying_a_rejected_recycle : Issue_87_seeded_stream
    {
        protected override async Task BecauseAsync()
        {
            Database.RunCommand<BsonDocument>(new BsonDocument { { "collMod", "Commits" },
                { "validator", new BsonDocument(MongoCommitFields.BucketId, new BsonDocument("$ne", MongoSystemBuckets.RecycleBin)) } });
            try
            {
                if (Async) await Engine.DeleteStreamAsync(Bucket, Stream, CancellationToken.None);
                else Engine.DeleteStream(Bucket, Stream);
            }
            catch (StorageException) { }
            Database.RunCommand<BsonDocument>(new BsonDocument { { "collMod", "Commits" }, { "validator", new BsonDocument() } });
            if (Async) await Engine.DeleteStreamAsync(Bucket, Stream, CancellationToken.None);
            else Engine.DeleteStream(Bucket, Stream);
        }

        [Fact]
        public void Retry_recycles_each_commit_once_and_removes_metadata()
        {
            Engine.GetDeletedCommits().Select(c => c.CheckpointToken).Should().Equal(1L, 2L);
            Engine.GetDeletedCommits().Should().OnlyContain(c => c.StreamId == Stream);
            Count("Streams", MongoStreamHeadFields.FullQualifiedBucketId, Bucket).Should().Be(0);
            Count("Snapshots", MongoSnapshotFields.FullQualifiedBucketId, Bucket).Should().Be(0);
        }
    }

    public class Issue_87_when_retrying_a_rejected_recycle_async : Issue_87_when_retrying_a_rejected_recycle
    {
        protected override bool Async => true;
    }
}
