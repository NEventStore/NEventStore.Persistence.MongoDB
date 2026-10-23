using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Persistence.MongoDB.Support;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public class Issue_87_when_retrying_a_partially_recycled_stream : Issue_87_seeded_stream
    {
        private Exception? _error;
        private List<BsonDocument> _original = null!;
        private List<BsonDocument> _afterFailure = null!;
        private List<BsonDocument> _afterRetry = null!;
        private long _headsAfterFailure;
        private long _snapshotsAfterFailure;
        private ICommit[] _deleted = null!;
        private ICommit[] _byOriginalIdentity = null!;

        protected override void Context()
        {
            base.Context();
            _original = ReadDocuments();
            // Seed the persisted state of an interrupted multi-document update explicitly.
            // UpdateMany does not guarantee visit order, so a validator alone cannot reliably
            // make the first commit succeed and the second fail on every MongoDB query plan.
            Database.GetCollection<BsonDocument>("Commits").UpdateOne(
                new BsonDocument(MongoCommitFields.CommitSequence, 1),
                Builders<BsonDocument>.Update.Set(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin)
                    .Set(MongoCommitFields.OriginalBucketId, Bucket)
                    .Set(MongoCommitFields.OriginalStreamId, Stream)
                    .Set(MongoCommitFields.StreamId, Guid.NewGuid().ToString("N")));
            Count("Streams", MongoStreamHeadFields.FullQualifiedBucketId, Bucket).Should().Be(1);
            Count("Snapshots", MongoSnapshotFields.FullQualifiedBucketId, Bucket).Should().Be(1);
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
            try { await Delete(); }
            catch (Exception error) { _error = error; }

            // Preserve the actual failed-operation state before retrying. Facts never mutate it.
            _afterFailure = ReadDocuments();
            _headsAfterFailure = Count("Streams", MongoStreamHeadFields.FullQualifiedBucketId, Bucket);
            _snapshotsAfterFailure = Count("Snapshots", MongoSnapshotFields.FullQualifiedBucketId, Bucket);
            Database.RunCommand<BsonDocument>(new BsonDocument
            {
                { "collMod", "Commits" },
                { "validator", new BsonDocument() }
            });
            await Delete();
            _afterRetry = ReadDocuments();
            if (Async)
            {
                var observer = new CommitStreamObserver();
                await Engine.GetDeletedCommitsAsync(observer, CancellationToken.None);
                _deleted = observer.Commits.ToArray();
                observer = new CommitStreamObserver();
                await Engine.GetFromAsync(MongoSystemBuckets.RecycleBin, Stream, 1, 4, observer, CancellationToken.None);
                _byOriginalIdentity = observer.Commits.ToArray();
            }
            else
            {
                _deleted = Engine.GetDeletedCommits().ToArray();
                _byOriginalIdentity = Engine.GetFrom(MongoSystemBuckets.RecycleBin, Stream, 1, 4).ToArray();
            }
        }

        [Fact]
        public void Failure_while_recycling_the_remaining_commit_is_propagated()
        {
            var cause = _error.Should().BeOfType<StorageException>().Which.InnerException;
            cause.Should().BeOfType<MongoWriteException>().Which.WriteError.Code.Should().Be(121);
        }

        [Fact]
        public void Rejected_retry_retains_one_recycled_commit_and_one_live_commit()
        {
            _afterFailure.Should().HaveCount(2);
            _afterFailure.Select(document => document[MongoCommitFields.BucketId].AsString)
                .Should().Equal(MongoSystemBuckets.RecycleBin, Bucket);
            _afterFailure.Select(document => document[MongoCommitFields.CommitSequence].AsInt32)
                .Should().Equal(1, 2);
        }

        [Fact]
        public void Failure_retains_the_live_head_and_snapshot_for_retry()
        {
            _headsAfterFailure.Should().Be(1);
            _snapshotsAfterFailure.Should().Be(1);
        }

        [Fact]
        public void Failure_and_retry_preserve_checkpoints_events_and_other_commit_data()
        {
            foreach (var documents in new[] { _afterFailure, _afterRetry })
            {
                documents.Select(document => document[MongoCommitFields.CheckpointNumber].AsInt64).Should().Equal(1L, 2L);
                for (var index = 0; index < _original.Count; index++)
                    foreach (var element in _original[index].Elements.Where(element =>
                        element.Name != MongoCommitFields.BucketId && element.Name != MongoCommitFields.StreamId))
                        documents[index][element.Name].Should().Be(element.Value, "recycling must preserve {0}", element.Name);
            }
        }

        [Fact]
        public void Retry_recycles_each_commit_once_and_removes_live_metadata()
        {
            _afterRetry.Should().HaveCount(2);
            _afterRetry.Should().OnlyContain(document => document[MongoCommitFields.BucketId].AsString == MongoSystemBuckets.RecycleBin);
            Count("Commits", MongoCommitFields.BucketId, Bucket).Should().Be(0);
            Engine.GetFrom(0).Should().BeEmpty();
            Count("Streams", MongoStreamHeadFields.FullQualifiedBucketId, Bucket).Should().Be(0);
            Count("Snapshots", MongoSnapshotFields.FullQualifiedBucketId, Bucket).Should().Be(0);
        }

        [Fact]
        public void Public_readers_rejoin_the_original_stream_with_its_original_checkpoints()
        {
            foreach (var commits in new[] { _deleted, _byOriginalIdentity })
            {
                commits.Select(commit => commit.CheckpointToken).Should().Equal(1L, 2L);
                commits.Should().OnlyContain(commit => commit.StreamId == Stream && commit.BucketId == MongoSystemBuckets.RecycleBin);
                commits.Select(commit => commit.CommitSequence).Should().Equal(1, 2);
            }
        }

        [Fact]
        public void Retry_preserves_the_already_recycled_document()
        {
            _afterRetry[0].Equals(_afterFailure[0]).Should().BeTrue("retry must not rewrite an already recycled commit");
        }

        [Fact]
        public void Retry_uses_separate_stored_groups_but_preserves_original_identity()
        {
            _afterRetry.Select(document => document[MongoCommitFields.StreamId].AsString)
                .Should().OnlyHaveUniqueItems().And.NotContain(Stream);
            _afterRetry.Should().OnlyContain(document =>
                document[MongoCommitFields.OriginalStreamId].AsString == Stream &&
                document[MongoCommitFields.OriginalBucketId].AsString == Bucket);
        }

        private List<BsonDocument> ReadDocuments() => Database.GetCollection<BsonDocument>("Commits")
            .Find(new BsonDocument()).Sort(new BsonDocument(MongoCommitFields.CheckpointNumber, 1)).ToList();

        private Task Delete()
        {
            if (Async)
                return Engine.DeleteStreamAsync(Bucket, Stream, CancellationToken.None);
            Engine.DeleteStream(Bucket, Stream);
            return Task.CompletedTask;
        }
    }

    public class Issue_87_when_retrying_a_partially_recycled_stream_async : Issue_87_when_retrying_a_partially_recycled_stream
    {
        protected override bool Async => true;
    }
}
