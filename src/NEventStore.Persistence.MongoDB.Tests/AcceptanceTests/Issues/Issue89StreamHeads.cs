using System.Collections.Concurrent;
using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using global::MongoDB.Driver.Core.Events;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public abstract class Issue_89_stream_head_concern : SpecificationBase
    {
        protected const string Stream = "reordered-stream";
        protected MongoPersistenceEngine Engine = null!;
        protected IMongoDatabase Database = null!;
        protected HeadWriteGate Gate = null!;
        protected MongoPersistenceOptions Options = null!;
        protected virtual bool Async => false;

        protected override void Context()
        {
            _ = new AcceptanceTestMongoPersistenceFactory();
            Gate = new HeadWriteGate();
            Options = new AcknowledgedMetadataOptions
            {
                PersistStreamHeadsOnBackgroundThread = false,
                ConfigureClientSettingsAction = settings => settings.ClusterConfigurator = cluster =>
                {
                    cluster.Subscribe<CommandStartedEvent>(Gate.Started);
                    cluster.Subscribe<CommandSucceededEvent>(Gate.Succeeded);
                    cluster.Subscribe<CommandFailedEvent>(Gate.Failed);
                }
            };
            var url = new MongoUrlBuilder(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
            {
                DatabaseName = $"issue89-{Guid.NewGuid():N}"
            };
            Database = Options.ConnectToDatabase(url.ToString());
            Engine = new MongoPersistenceEngine(Database, new DocumentObjectSerializer(), Options);
            Engine.Initialize();
        }

        protected Task<ICommit?> Commit(CommitAttempt attempt)
        {
            if (Async) return Engine.CommitAsync(attempt, CancellationToken.None);
            return Task.FromResult(Engine.Commit(attempt));
        }

        protected Task<bool> Snapshot(int revision)
        {
            var snapshot = new Snapshot(Bucket.Default, Stream, revision, "snapshot");
            if (Async) return Engine.AddSnapshotAsync(snapshot, CancellationToken.None);
            return Task.FromResult(Engine.AddSnapshot(snapshot));
        }

        protected BsonDocument ReadHead() => Database.GetCollection<BsonDocument>("Streams")
            .Find(new BsonDocument()).Single();

        protected override void Cleanup()
        {
            Gate?.Release.TrySetResult(true);
            Engine?.Dispose();
            if (Database != null) Database.Client.DropDatabase(Database.DatabaseNamespace.DatabaseName);
        }

        protected static async Task WaitFor(Task task)
        {
            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30))).ConfigureAwait(false) != task)
                throw new TimeoutException("A stream-head command did not reach its deterministic gate.");
            await task.ConfigureAwait(false);
        }

        private sealed class AcknowledgedMetadataOptions : MongoPersistenceOptions
        {
            public override MongoCollectionSettings GetStreamSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
            public override MongoCollectionSettings GetSnapshotSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
        }

        // Gate the real driver's wire command, before sending it to MongoDB. Success events
        // come from acknowledged writes, so completion means the server applied the update.
        protected sealed class HeadWriteGate
        {
            private readonly ConcurrentDictionary<int, TaskCompletionSource<bool>> _pending = new();
            private int _oldRevision;
            private int _newRevision;
            private int _writes;
            public TaskCompletionSource<bool> Entered { get; } = NewGate();
            public TaskCompletionSource<bool> Release { get; } = NewGate();
            public TaskCompletionSource<bool> OlderCompleted { get; } = NewGate();
            public TaskCompletionSource<bool> NewerCompleted { get; } = NewGate();
            public int Writes => Volatile.Read(ref _writes);

            public void Arm(int oldRevision, int newRevision)
            {
                _oldRevision = oldRevision;
                _newRevision = newRevision;
            }

            public void Started(CommandStartedEvent command)
            {
                if (command.CommandName != "update" || command.Command["update"] != "Streams") return;
                var value = command.Command["updates"][0]["u"];
                if (!value.IsBsonDocument) return;
                var update = value.AsBsonDocument;
                BsonValue revision;
                // Accept the old $set and the fixed $max so the same test reproduces the bug.
                if (!(update.TryGetValue("$set", out var fields) && fields.AsBsonDocument.TryGetValue(MongoStreamHeadFields.HeadRevision, out revision)) &&
                    !(update.TryGetValue("$max", out fields) && fields.AsBsonDocument.TryGetValue(MongoStreamHeadFields.HeadRevision, out revision))) return;
                Interlocked.Increment(ref _writes);
                if (revision.AsInt32 != _oldRevision && revision.AsInt32 != _newRevision) return;
                _pending[command.RequestId] = revision.AsInt32 == _oldRevision ? OlderCompleted : NewerCompleted;
                if (revision.AsInt32 != _oldRevision) return;
                Entered.TrySetResult(true);
                if (!Release.Task.Wait(TimeSpan.FromSeconds(30)))
                    OlderCompleted.TrySetException(new TimeoutException("The older stream-head write was not released."));
            }

            public void Succeeded(CommandSucceededEvent command)
            {
                if (_pending.TryRemove(command.RequestId, out var completion)) completion.TrySetResult(true);
            }

            public void Failed(CommandFailedEvent command)
            {
                if (_pending.TryRemove(command.RequestId, out var completion)) completion.TrySetException(command.Failure);
            }

            private static TaskCompletionSource<bool> NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public class Issue_89_when_head_writes_complete_in_reverse_order : Issue_89_stream_head_concern
    {
        protected virtual bool ExistingHead => false;
        protected virtual bool ExistingSnapshot => false;
        private CommitAttempt _older = null!;
        private CommitAttempt _newer = null!;
        private BsonDocument _beforeRelease = null!;
        private BsonDocument _afterRelease = null!;
        private Exception? _retryError;
        private int _writesBeforeRetry;

        protected override async Task ContextAsync()
        {
            _older = Stream.BuildAttempt();
            if (ExistingHead)
            {
                await Commit(_older);
                if (ExistingSnapshot) (await Snapshot(_older.StreamRevision)).Should().BeTrue();
                _older = _older.BuildNextAttempt();
            }
            _newer = _older.BuildNextAttempt();
            Gate.Arm(_older.StreamRevision, _newer.StreamRevision);
            Options.PersistStreamHeadsOnBackgroundThread = true;
        }

        protected override async Task BecauseAsync()
        {
            // Task.Run also keeps the async caller independent if the driver raises its
            // command event synchronously before the first incomplete await.
            var olderCommit = Task.Run(() => Commit(_older));
            try
            {
                await WaitFor(Gate.Entered.Task);
                await Commit(_newer);
                await WaitFor(Gate.NewerCompleted.Task);
                _beforeRelease = ReadHead();
            }
            finally
            {
                Gate.Release.TrySetResult(true);
                await WaitFor(Gate.OlderCompleted.Task);
                await WaitFor(olderCommit);
            }
            _afterRelease = ReadHead();
            _writesBeforeRetry = Gate.Writes;
            Options.PersistStreamHeadsOnBackgroundThread = false;
            try { await Commit(_older); }
            catch (Exception error) { _retryError = error; }
        }

        [Fact]
        public void Older_completion_cannot_hide_a_newer_acknowledged_revision()
        {
            _beforeRelease[MongoStreamHeadFields.HeadRevision].AsInt32.Should().Be(_newer.StreamRevision);
            _afterRelease[MongoStreamHeadFields.HeadRevision].AsInt32.Should().Be(_newer.StreamRevision);
        }

        [Fact]
        public void Both_writes_contribute_their_events_without_overwriting_snapshot_metadata()
        {
            var snapshotRevision = ExistingSnapshot ? 2 : 0;
            _afterRelease[MongoStreamHeadFields.SnapshotRevision].AsInt32.Should().Be(snapshotRevision);
            _afterRelease[MongoStreamHeadFields.Unsnapshotted].AsInt32.Should().Be(_newer.StreamRevision - snapshotRevision);
            _afterRelease[MongoStreamHeadFields.Unsnapshotted].AsInt32.Should()
                .Be(_beforeRelease[MongoStreamHeadFields.Unsnapshotted].AsInt32 + _older.Events.Count);
        }

        [Fact]
        public void Duplicate_commit_retry_does_not_increment_metadata_again()
        {
            _retryError.Should().BeOfType<DuplicateCommitException>();
            Gate.Writes.Should().Be(_writesBeforeRetry);
            ReadHead().Equals(_afterRelease).Should().BeTrue();
            Engine.GetFrom(Bucket.Default, Stream, 0, int.MaxValue).Should().HaveCount(ExistingHead ? 3 : 2);
        }
    }

    public class Issue_89_when_head_writes_complete_in_reverse_order_async : Issue_89_when_head_writes_complete_in_reverse_order
    {
        protected override bool Async => true;
    }
    public class Issue_89_when_existing_head_writes_complete_in_reverse_order : Issue_89_when_head_writes_complete_in_reverse_order
    {
        protected override bool ExistingHead => true;
    }
    public class Issue_89_when_existing_head_writes_complete_in_reverse_order_async : Issue_89_when_existing_head_writes_complete_in_reverse_order
    {
        protected override bool Async => true;
    }
    public class Issue_89_when_snapshotted_head_writes_complete_in_reverse_order : Issue_89_when_existing_head_writes_complete_in_reverse_order
    {
        protected override bool ExistingSnapshot => true;
    }
    public class Issue_89_when_snapshotted_head_writes_complete_in_reverse_order_async : Issue_89_when_snapshotted_head_writes_complete_in_reverse_order
    {
        protected override bool Async => true;
    }

    public class Issue_89_when_inserting_a_stream_head : Issue_89_stream_head_concern
    {
        protected virtual bool Recreate => false;
        protected override async Task ContextAsync()
        {
            if (!Recreate) return;
            var initial = Stream.BuildAttempt();
            await Commit(initial);
            await Commit(initial.BuildNextAttempt());
            (await Snapshot(2)).Should().BeTrue();
            // All metadata writes have drained. Concurrent deletion/recreation is #96.
            if (Async) await Engine.DeleteStreamAsync(Bucket.Default, Stream, CancellationToken.None);
            else Engine.DeleteStream(Bucket.Default, Stream);
        }

        protected override async Task BecauseAsync() => await Commit(Stream.BuildAttempt());

        [Fact]
        public void Upsert_initializes_all_counters_for_this_stream_lifetime()
        {
            var head = ReadHead();
            head[MongoStreamHeadFields.HeadRevision].AsInt32.Should().Be(2);
            head[MongoStreamHeadFields.SnapshotRevision].AsInt32.Should().Be(0);
            head[MongoStreamHeadFields.Unsnapshotted].AsInt32.Should().Be(2);
        }
    }
    public class Issue_89_when_inserting_a_stream_head_async : Issue_89_when_inserting_a_stream_head
    {
        protected override bool Async => true;
    }
    public class Issue_89_when_recreating_a_stream_head : Issue_89_when_inserting_a_stream_head
    {
        protected override bool Recreate => true;
    }
    public class Issue_89_when_recreating_a_stream_head_async : Issue_89_when_recreating_a_stream_head
    {
        protected override bool Async => true;
    }
}
