using FluentAssertions;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    // Compiled only by the integration CI job, with the pinned #89 implementation and fixtures.
    public class Combined_metadata_when_an_older_commit_finishes_after_a_snapshot : Issue_89_stream_head_concern
    {
        private CommitAttempt _older = null!;
        private CommitAttempt _newer = null!;

        protected override Task ContextAsync()
        {
            _older = Stream.BuildAttempt();
            _newer = _older.BuildNextAttempt();
            Gate.Arm(_older.StreamRevision, _newer.StreamRevision);
            Options.PersistStreamHeadsOnBackgroundThread = true;
            return Task.CompletedTask;
        }

        protected override async Task BecauseAsync()
        {
            var olderCommit = Task.Run(() => Commit(_older));
            try
            {
                await WaitFor(Gate.Entered.Task);
                await Commit(_newer);
                await WaitFor(Gate.NewerCompleted.Task);
                (await Snapshot(_newer.StreamRevision)).Should().BeTrue();
                ReadHead()[MongoStreamHeadFields.Unsnapshotted].AsInt32.Should().Be(-_older.Events.Count);
            }
            finally
            {
                Gate.Release.TrySetResult(true);
                await WaitFor(Gate.OlderCompleted.Task);
                await WaitFor(olderCommit);
            }
        }

        [Fact]
        public void Completed_commit_and_snapshot_updates_agree_on_the_public_candidate_state()
        {
            var head = ReadHead();
            head[MongoStreamHeadFields.HeadRevision].AsInt32.Should().Be(_newer.StreamRevision);
            head[MongoStreamHeadFields.SnapshotRevision].AsInt32.Should().Be(_newer.StreamRevision);
            head[MongoStreamHeadFields.Unsnapshotted].AsInt32.Should().Be(0);
            Engine.GetStreamsToSnapshot(Bucket.Default, 1).Should().BeEmpty();
        }
    }

    public class Combined_metadata_when_an_older_commit_finishes_after_a_snapshot_async
        : Combined_metadata_when_an_older_commit_finishes_after_a_snapshot
    {
        protected override bool Async => true;
    }
}
