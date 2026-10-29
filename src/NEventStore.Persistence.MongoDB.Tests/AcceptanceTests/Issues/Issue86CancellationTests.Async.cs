using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Persistence.MongoDB.Support;
using NEventStore.Serialization;
#if NUNIT
using NUnit.Framework;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues.Async
{
    public abstract class Issue_86_concern : SpecificationBase
    {
        protected CancellationEngine Engine = null!;
        protected CancellationTokenSource Cancellation = null!;
        protected CommitAttempt Attempt = null!;
        protected Task Operation = null!;
        protected Exception? Error;
        protected IMongoDatabase Database = null!;
        protected readonly Checkpoints Generator = new();
        private MongoClient _client = null!;

        protected override void Context()
        {
            // Register the acceptance suite's serializers before using real MongoDB collections.
            _ = new AcceptanceTestMongoPersistenceFactory();
            var url = new MongoUrl(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
            _client = new MongoClient(url);
            Database = _client.GetDatabase(url.DatabaseName + "_issue86_" + Guid.NewGuid().ToString("N"));
            Engine = new CancellationEngine(Database, new DocumentObjectSerializer(), new SnapshotOptions
            {
                CheckpointGenerator = Generator,
                PersistStreamHeadsOnBackgroundThread = false
            });
            Engine.Initialize();
            Cancellation = new CancellationTokenSource();
            Attempt = Guid.NewGuid().ToString().BuildAttempt();
        }

        protected override void Cleanup()
        {
            Engine?.Dispose();
            Cancellation?.Dispose();
            if (Database != null) _client.DropDatabase(Database.DatabaseNamespace.DatabaseName);
        }

        protected static async Task WaitFor(Task task)
        {
            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false) != task)
                throw new TimeoutException("The deterministic cancellation gate was not reached.");
            await task.ConfigureAwait(false);
        }

        protected static TaskCompletionSource<bool> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected class Checkpoints : ICheckpointGenerator
        {
            private long _next;
            public Func<CancellationToken, Task<long>>? GenerateAsync { get; set; }
            public long Next() => Interlocked.Increment(ref _next);
            public Task<long> NextAsync(CancellationToken cancellationToken = default) =>
                GenerateAsync?.Invoke(cancellationToken) ?? Task.FromResult(Next());
            public void SignalDuplicateId(long id) => throw new InvalidOperationException("Unexpected checkpoint collision.");
            public Task SignalDuplicateIdAsync(long id, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("Unexpected checkpoint collision.");
        }

        protected class CancellationEngine : MongoPersistenceEngine
        {
            public CancellationEngine(IMongoDatabase database, IDocumentSerializer serializer, MongoPersistenceOptions options)
                : base(database, serializer, options) { }
            public Task Execute(Func<Task> callback, CancellationToken token, IAsyncObserver<int>? observer = null) =>
                TryMongoAsync(callback, token, observer);
            public Task<int> ExecuteValue(Func<Task<int>> callback) => TryMongoAsync(callback);
        }

        private class SnapshotOptions : MongoPersistenceOptions
        {
            // Make test setup and postconditions deterministic; production write concerns are unchanged.
            public override MongoCollectionSettings GetSnapshotSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
            public override MongoCollectionSettings GetStreamSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
        }
    }

    [TestFixture(false, false)]
    [TestFixture(false, true)]
    [TestFixture(true, false)]
    [TestFixture(true, true)]
    public class when_Issue_86_commit_checkpoint_generation_is_canceled : Issue_86_concern
    {
        private readonly bool _inFlight;
        private readonly bool _operationCanceledException;
        private readonly TaskCompletionSource<bool> _entered = Gate();
        private readonly TaskCompletionSource<bool> _release = Gate();
        private long _persisted;
        private bool _wasPending;

        public when_Issue_86_commit_checkpoint_generation_is_canceled(bool inFlight, bool operationCanceledException)
        {
            _inFlight = inFlight;
            _operationCanceledException = operationCanceledException;
        }

        protected override void Context()
        {
            base.Context();
            Generator.GenerateAsync = async token =>
            {
                _entered.SetResult(true);
                if (_inFlight) await _release.Task.ConfigureAwait(false);
                if (_operationCanceledException) throw new OperationCanceledException(token);
                return await Task.FromCanceled<long>(token).ConfigureAwait(false);
            };
            if (!_inFlight) Cancellation.Cancel();
        }

        protected override async Task BecauseAsync()
        {
            Operation = Engine.CommitAsync(Attempt, Cancellation.Token);
            await WaitFor(_entered.Task).ConfigureAwait(false);
            _wasPending = !Operation.IsCompleted;
            Cancellation.Cancel();
            _release.SetResult(true);
            Error = await Catch.ExceptionAsync(() => Operation).ConfigureAwait(false);
            _persisted = Database.GetCollection<BsonDocument>("Commits").CountDocuments(FilterDefinition<BsonDocument>.Empty);
        }

        [Fact]
        public void Should_return_a_canceled_task_instead_of_a_successful_null_commit()
        {
            Operation.IsCanceled.Should().BeTrue();
            Error.Should().BeAssignableTo<OperationCanceledException>();
            ((OperationCanceledException)Error!).CancellationToken.Should().Be(Cancellation.Token);
        }

        [Fact]
        public void Should_not_write_the_canceled_commit() => _persisted.Should().Be(0);

        [Fact]
        public void Should_cancel_after_the_checkpoint_operation_started() => _wasPending.Should().Be(_inFlight);
    }

    [TestFixture(false, false, false)]
    [TestFixture(false, false, true)]
    [TestFixture(false, true, false)]
    [TestFixture(false, true, true)]
    [TestFixture(true, false, false)]
    [TestFixture(true, false, true)]
    [TestFixture(true, true, false)]
    [TestFixture(true, true, true)]
    public class when_Issue_86_a_wrapped_operation_is_canceled : Issue_86_concern
    {
        private readonly bool _hasResult;
        private readonly bool _inFlight;
        private readonly bool _operationCanceledException;
        private readonly TaskCompletionSource<bool> _entered = Gate();
        private readonly TaskCompletionSource<bool> _release = Gate();
        private bool _wasPending;

        public when_Issue_86_a_wrapped_operation_is_canceled(bool hasResult, bool inFlight, bool operationCanceledException)
        {
            _hasResult = hasResult;
            _inFlight = inFlight;
            _operationCanceledException = operationCanceledException;
        }

        protected override void Context()
        {
            base.Context();
            if (!_inFlight) Cancellation.Cancel();
        }

        private async Task<int> CancelOperation()
        {
            _entered.SetResult(true);
            if (_inFlight) await _release.Task.ConfigureAwait(false);
            if (_operationCanceledException) throw new OperationCanceledException(Cancellation.Token);
            return await Task.FromCanceled<int>(Cancellation.Token).ConfigureAwait(false);
        }

        protected override async Task BecauseAsync()
        {
            Operation = _hasResult ? Engine.ExecuteValue(CancelOperation) : Engine.Execute(async () =>
            {
                await CancelOperation().ConfigureAwait(false);
            }, Cancellation.Token);
            await WaitFor(_entered.Task).ConfigureAwait(false);
            _wasPending = !Operation.IsCompleted;
            Cancellation.Cancel();
            _release.SetResult(true);
            Error = await Catch.ExceptionAsync(() => Operation).ConfigureAwait(false);
        }

        [Fact]
        public void Should_preserve_the_canceled_task_and_token()
        {
            Operation.IsCanceled.Should().BeTrue();
            Error.Should().BeAssignableTo<OperationCanceledException>();
            ((OperationCanceledException)Error!).CancellationToken.Should().Be(Cancellation.Token);
        }

        [Fact]
        public void Should_exercise_cancellation_at_the_requested_boundary() => _wasPending.Should().Be(_inFlight);
    }

    public class when_Issue_86_a_derived_engine_completes_its_own_callback : Issue_86_concern
    {
        private int _completed;
        private IAsyncObserver<int> _observer = null!;

        protected override void Context()
        {
            base.Context();
            _observer = new LambdaAsyncObserver<int>((_, _) => Task.FromResult(true), onCompletedAsync: _ =>
            {
                _completed++;
                return Task.CompletedTask;
            });
        }

        protected override Task BecauseAsync() => Engine.Execute(() => _observer.OnCompletedAsync(Cancellation.Token), Cancellation.Token, _observer);

        [Fact]
        public void Should_not_add_completion_to_the_protected_wrapper_success_path() => _completed.Should().Be(1);
    }

    [TestFixture("snapshot-read")]
    [TestFixture("snapshot-write")]
    [TestFixture("purge")]
    [TestFixture("purge-bucket")]
    [TestFixture("delete-stream")]
    [TestFixture("empty-recycle-bin")]
    public class when_Issue_86_an_ordinary_operation_is_already_canceled : Issue_86_concern
    {
        private readonly string _operation;
        private long _commits;
        private long _snapshots;
        private ISnapshot _snapshot = null!;

        public when_Issue_86_an_ordinary_operation_is_already_canceled(string operation) => _operation = operation;

        protected override void Context()
        {
            base.Context();
            Engine.Commit(Attempt);
            _snapshot = new Snapshot(Attempt.BucketId, Attempt.StreamId, 1, "snapshot");
            Cancellation.Cancel();
        }

        protected override async Task BecauseAsync()
        {
            Operation = _operation switch
            {
                "snapshot-read" => Engine.GetSnapshotAsync(Attempt.BucketId, Attempt.StreamId, 1, Cancellation.Token),
                "snapshot-write" => Engine.AddSnapshotAsync(_snapshot, Cancellation.Token),
                "purge" => Engine.PurgeAsync(Cancellation.Token),
                "purge-bucket" => Engine.PurgeAsync(Attempt.BucketId, Cancellation.Token),
                "delete-stream" => Engine.DeleteStreamAsync(Attempt.BucketId, Attempt.StreamId, Cancellation.Token),
                _ => Engine.EmptyRecycleBinAsync(Cancellation.Token)
            };
            Error = await Catch.ExceptionAsync(() => Operation).ConfigureAwait(false);
            _commits = Database.GetCollection<BsonDocument>("Commits").CountDocuments(FilterDefinition<BsonDocument>.Empty);
            _snapshots = Database.GetCollection<BsonDocument>("Snapshots").CountDocuments(FilterDefinition<BsonDocument>.Empty);
        }

        [Fact]
        public void Should_not_disguise_cancellation_as_success_or_missing_data()
        {
            Operation.IsCanceled.Should().BeTrue();
            Error.Should().BeAssignableTo<OperationCanceledException>();
        }

        [Fact]
        public void Should_leave_persisted_data_unchanged()
        {
            _commits.Should().Be(1);
            _snapshots.Should().Be(0);
        }
    }

    [TestFixture("stream", "canceled")]
    [TestFixture("bucket", "canceled")]
    [TestFixture("bucket-range", "canceled")]
    [TestFixture("all", "canceled")]
    [TestFixture("range", "canceled")]
    [TestFixture("heads", "canceled")]
    [TestFixture("deleted", "canceled")]
    [TestFixture("stream", "stop")]
    [TestFixture("bucket", "stop")]
    [TestFixture("bucket-range", "stop")]
    [TestFixture("all", "stop")]
    [TestFixture("range", "stop")]
    [TestFixture("heads", "stop")]
    [TestFixture("deleted", "stop")]
    [TestFixture("stream", "success")]
    [TestFixture("bucket", "success")]
    [TestFixture("bucket-range", "success")]
    [TestFixture("all", "success")]
    [TestFixture("range", "success")]
    [TestFixture("heads", "success")]
    [TestFixture("deleted", "success")]
    [TestFixture("stream", "next-task-canceled")]
    [TestFixture("stream", "next-operation-canceled")]
    [TestFixture("stream", "next-unrelated-task-canceled")]
    [TestFixture("stream", "next-unrelated-operation-canceled")]
    [TestFixture("stream", "next-error")]
    [TestFixture("stream", "completion-task-canceled")]
    [TestFixture("stream", "completion-operation-canceled")]
    [TestFixture("stream", "completion-error")]
    [TestFixture("stream", "error-callback-error")]
    [TestFixture("stream", "error-callback-canceled")]
    public class when_Issue_86_an_observer_receives_a_terminal_notification : Issue_86_concern
    {
        private readonly string _read;
        private readonly string _mode;
        private readonly TaskCompletionSource<bool> _terminalEntered = Gate();
        private readonly TaskCompletionSource<bool> _terminalRelease = Gate();
        private readonly TaskCompletionSource<bool> _nextEntered = Gate();
        private readonly TaskCompletionSource<bool> _nextRelease = Gate();
        private readonly Exception _failure = new InvalidOperationException("Observer failure.");
        private int _completed;
        private int _errors;
        private int _next;
        private bool _pendingAtTerminal;
        private bool _pendingAtNext;
        private CancellationToken _terminalToken;
        private Exception? _reportedError;
        private Exception? _unrelatedCancellation;
        private IAsyncObserver<ICommit> _commits = null!;
        private IAsyncObserver<IStreamHead> _heads = null!;

        public when_Issue_86_an_observer_receives_a_terminal_notification(string read, string mode)
        {
            _read = read;
            _mode = mode;
        }

        protected override void Context()
        {
            base.Context();
            Engine.Commit(Attempt);
            if (_read == "heads") Engine.CommitSingle();
            else Engine.CommitNext(Attempt);
            if (_read == "deleted") Engine.DeleteStream(Attempt.BucketId, Attempt.StreamId);
            if (_mode == "canceled") Cancellation.Cancel();
            _commits = new LambdaAsyncObserver<ICommit>((_, token) => OnNext(token), OnError, OnCompleted);
            _heads = new LambdaAsyncObserver<IStreamHead>((_, token) => OnNext(token), OnError, OnCompleted);
        }

        private async Task<bool> OnNext(CancellationToken token)
        {
            _next++;
            if (_mode.StartsWith("next-", StringComparison.Ordinal))
            {
                _nextEntered.TrySetResult(true);
                await _nextRelease.Task.ConfigureAwait(false);
                if (_mode == "next-task-canceled") return await Task.FromCanceled<bool>(token).ConfigureAwait(false);
                if (_mode == "next-operation-canceled") throw new OperationCanceledException(token);
                if (_mode == "next-unrelated-task-canceled" || _mode == "next-unrelated-operation-canceled")
                {
                    using var unrelatedCancellation = new CancellationTokenSource();
                    unrelatedCancellation.Cancel();
                    if (_mode == "next-unrelated-task-canceled")
                    {
                        try
                        {
                            return await Task.FromCanceled<bool>(unrelatedCancellation.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException exception)
                        {
                            _unrelatedCancellation = exception;
                            throw;
                        }
                    }
                    _unrelatedCancellation = new OperationCanceledException(unrelatedCancellation.Token);
                    throw _unrelatedCancellation;
                }
                throw _failure;
            }
            if (_mode.StartsWith("error-callback-", StringComparison.Ordinal)) throw _failure;
            return _mode != "stop";
        }

        private async Task OnCompleted(CancellationToken token)
        {
            _completed++;
            _terminalToken = token;
            _terminalEntered.TrySetResult(true);
            await _terminalRelease.Task.ConfigureAwait(false);
            if (_mode == "completion-task-canceled") await Task.FromCanceled(token).ConfigureAwait(false);
            if (_mode == "completion-operation-canceled") throw new OperationCanceledException(token);
            if (_mode == "completion-error") throw _failure;
        }

        private async Task OnError(Exception error, CancellationToken token)
        {
            _errors++;
            _reportedError = error;
            _terminalToken = token;
            _terminalEntered.TrySetResult(true);
            await _terminalRelease.Task.ConfigureAwait(false);
            if (_mode == "error-callback-error") throw _failure;
            if (_mode == "error-callback-canceled") await Task.FromCanceled(token).ConfigureAwait(false);
        }

        protected override async Task BecauseAsync()
        {
            Operation = _read switch
            {
                "stream" => Engine.GetFromAsync(Attempt.BucketId, Attempt.StreamId, 0, int.MaxValue, _commits, Cancellation.Token),
                "bucket" => Engine.GetFromAsync(Attempt.BucketId, 0, _commits, Cancellation.Token),
                "bucket-range" => Engine.GetFromToAsync(Attempt.BucketId, 0, long.MaxValue, _commits, Cancellation.Token),
                "all" => Engine.GetFromAsync(0, _commits, Cancellation.Token),
                "range" => Engine.GetFromToAsync(0, long.MaxValue, _commits, Cancellation.Token),
                "heads" => Engine.GetStreamsToSnapshotAsync(Attempt.BucketId, 1, _heads, Cancellation.Token),
                _ => Engine.GetDeletedCommitsAsync(_commits, Cancellation.Token)
            };
            if (_mode.StartsWith("next-", StringComparison.Ordinal))
            {
                await WaitFor(_nextEntered.Task).ConfigureAwait(false);
                _pendingAtNext = !Operation.IsCompleted;
                if (_mode != "next-error" && !_mode.StartsWith("next-unrelated-", StringComparison.Ordinal)) Cancellation.Cancel();
                _nextRelease.SetResult(true);
            }
            await WaitFor(_terminalEntered.Task).ConfigureAwait(false);
            _pendingAtTerminal = !Operation.IsCompleted;
            if (_mode.StartsWith("completion-", StringComparison.Ordinal) || _mode == "error-callback-canceled") Cancellation.Cancel();
            _terminalRelease.SetResult(true);
            Error = await Catch.ExceptionAsync(() => Operation).ConfigureAwait(false);
        }

        [Fact]
        public void Should_await_the_terminal_callback_and_preserve_the_original_token()
        {
            _pendingAtTerminal.Should().BeTrue();
            _terminalToken.Should().Be(Cancellation.Token);
        }

        [Fact]
        public void Should_deliver_exactly_one_terminal_notification()
        {
            bool unrelatedCancellation = _mode.StartsWith("next-unrelated-", StringComparison.Ordinal);
            bool dataError = _mode == "next-error" || unrelatedCancellation || _mode.StartsWith("error-callback-", StringComparison.Ordinal);
            _completed.Should().Be(dataError ? 0 : 1);
            _errors.Should().Be(dataError ? 1 : 0);
            _reportedError.Should().BeSameAs(unrelatedCancellation ? _unrelatedCancellation : dataError ? _failure : null);
            if (unrelatedCancellation)
            {
                _reportedError.Should().BeOfType(_mode == "next-unrelated-task-canceled" ? typeof(TaskCanceledException) : typeof(OperationCanceledException));
                ((OperationCanceledException)_reportedError!).CancellationToken.Should().NotBe(Cancellation.Token);
                Cancellation.IsCancellationRequested.Should().BeFalse();
            }
        }

        [Fact]
        public void Should_preserve_callback_failures_without_renotifying_the_observer()
        {
            if (_mode == "completion-task-canceled" || _mode == "completion-operation-canceled" || _mode == "error-callback-canceled")
            {
                Error.Should().BeAssignableTo<OperationCanceledException>();
                Operation.IsCanceled.Should().BeTrue();
            }
            else if (_mode == "completion-error" || _mode == "error-callback-error")
                Error.Should().BeSameAs(_failure);
            else
                Error.Should().BeNull("data cancellation is observer completion, not ordinary task cancellation");
        }

        [Fact]
        public void Should_stop_reading_when_canceled_or_requested_by_the_observer()
        {
            _next.Should().Be(_mode == "canceled" ? 0 : _mode == "success" || _mode.StartsWith("completion-", StringComparison.Ordinal) ? 2 : 1);
            _pendingAtNext.Should().Be(_mode.StartsWith("next-", StringComparison.Ordinal));
        }
    }

    [TestFixture("commit", false)]
    [TestFixture("commit", true)]
    [TestFixture("snapshot", false)]
    [TestFixture("snapshot", true)]
    [TestFixture("missing-snapshot", false)]
    [TestFixture("missing-snapshot", true)]
    [TestFixture("invalid-snapshot", false)]
    [TestFixture("invalid-snapshot", true)]
    [TestFixture("null-snapshot", false)]
    [TestFixture("null-snapshot", true)]
    public class when_Issue_86_an_ordinary_operation_is_not_canceled : Issue_86_concern
    {
        private readonly string _operation;
        private object? _result;
        private readonly bool _synchronous;
        private ISnapshot _snapshot = null!;
        public when_Issue_86_an_ordinary_operation_is_not_canceled(string operation, bool synchronous)
        {
            _operation = operation;
            _synchronous = synchronous;
        }

        protected override void Context()
        {
            base.Context();
            if (_operation == "snapshot") Engine.Commit(Attempt);
            _snapshot = _operation == "null-snapshot" ? null! : new Snapshot(Attempt.BucketId, Attempt.StreamId, 1, "snapshot");
        }

        protected override async Task BecauseAsync()
        {
            if (_operation == "commit") _result = _synchronous ? Engine.Commit(Attempt) : await Engine.CommitAsync(Attempt, CancellationToken.None).ConfigureAwait(false);
            else if (_operation == "missing-snapshot")
            {
                _result = _synchronous ? Engine.GetSnapshot(Attempt.BucketId, Attempt.StreamId, 1) :
                    await Engine.GetSnapshotAsync(Attempt.BucketId, Attempt.StreamId, 1, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                _result = _synchronous ? Engine.AddSnapshot(_snapshot) :
                    await Engine.AddSnapshotAsync(_snapshot, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [Fact]
        public void Should_preserve_success_and_non_cancellation_negative_results()
        {
            if (_operation == "commit") _result.Should().BeAssignableTo<ICommit>();
            else if (_operation == "snapshot") _result.Should().Be(true);
            else if (_operation == "missing-snapshot") _result.Should().BeNull();
            else _result.Should().Be(false);
        }

    }
}
#endif
