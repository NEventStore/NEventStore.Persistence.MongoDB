#if NUNIT
using FakeItEasy;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
using NUnit.Framework;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    // Application failures must not be misidentified as event-store outages.
    [TestFixtureSource(nameof(Cases))]
    public class when_Issue108_an_async_read_fails(int path, string stage, string kind, bool legacy) : SpecificationBase
    {
        public static IEnumerable<object[]> Cases =>
            from path in Enumerable.Range(0, 7)
            from stage in new[] { "serializer", "observer throw", "observer task", "observer delayed", "early stop", "find", "later batch", "dispose" }
            where path != 5 || stage != "serializer"
            from kind in new[] { "mongo", "connection", "other" }
            where stage != "early stop" || kind == "other"
            from legacy in new[] { false, true }
            select new object[] { path, stage, kind, legacy };

        private readonly CancellationTokenSource _cancellation = new();
        private readonly IAsyncCursor<BsonDocument> _cursor = A.Fake<IAsyncCursor<BsonDocument>>();
        private ProbeEngine _engine = null!;
        private Exception _failure = null!;
        private Exception? _reported;
        private Exception? _escaped;
        private readonly TaskCompletionSource<bool> _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _nextCalls;
        private int _errors;
        private int _completions;

        protected override void Context()
        {
            _failure = EnumerationProbe.Failure(kind);
            var database = A.Fake<IMongoDatabase>();
            var collection = A.Fake<IMongoCollection<BsonDocument>>();
            var serializer = A.Fake<IDocumentSerializer>();
            A.CallTo(() => collection.Settings).Returns(new MongoCollectionSettings());
            A.CallTo(() => collection.WithWriteConcern(A<WriteConcern>._)).Returns(collection);
            A.CallTo(() => database.GetCollection<BsonDocument>(A<string>._, A<MongoCollectionSettings>._)).Returns(collection);
            A.CallTo(() => serializer.Deserialize<EventMessage>(A<object>._)).Throws(_failure);
            A.CallTo(() => collection.FindAsync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, _cancellation.Token))
                .ReturnsLazily(() => stage == "find"
                    ? Task.FromException<IAsyncCursor<BsonDocument>>(_failure)
                    : Task.FromResult(_cursor));
            int batch = 0;
            A.CallTo(() => _cursor.MoveNextAsync(_cancellation.Token)).ReturnsLazily(() =>
            {
                batch++;
                return batch == 2 && stage == "later batch"
                    ? Task.FromException<bool>(_failure) : Task.FromResult(batch == 1);
            });
            A.CallTo(() => _cursor.Current).Returns(new[] { Document(path == 5 ? 7 : 0, 1, stage == "serializer") });
            if (stage == "dispose") A.CallTo(() => _cursor.Dispose()).Throws(_failure);
            _engine = new ProbeEngine(database, serializer, legacy);
        }

        private IAsyncObserver<T> Observer<T>()
        {
            var observer = A.Fake<IAsyncObserver<T>>();
            A.CallTo(() => observer.OnNextAsync(A<T>._, _cancellation.Token)).ReturnsLazily(() =>
            {
                _nextCalls++;
                if (stage == "observer throw") throw _failure;
                if (stage == "observer delayed") return FailAfterResumeAsync();
                if (stage == "early stop") return Task.FromResult(false);
                return stage == "observer task" ? Task.FromException<bool>(_failure) : Task.FromResult(true);
            });
            A.CallTo(() => observer.OnErrorAsync(A<Exception>._, _cancellation.Token))
                .ReturnsLazily((Exception error, CancellationToken token) =>
                {
                    _reported = error;
                    _errors++;
                    return Task.CompletedTask;
                });
            A.CallTo(() => observer.OnCompletedAsync(_cancellation.Token)).ReturnsLazily(() =>
            {
                _completions++;
                return Task.CompletedTask;
            });
            return observer;
        }

        private async Task<bool> FailAfterResumeAsync()
        {
            await _resume.Task.ConfigureAwait(false);
            throw _failure;
        }

        protected override async Task BecauseAsync()
        {
            var observer = Observer<ICommit>();
            var operation = Catch.ExceptionAsync(() => path switch
            {
                0 => _engine.GetFromAsync("bucket", "stream", 0, int.MaxValue, observer, _cancellation.Token),
                1 => _engine.GetFromAsync("bucket", 0L, observer, _cancellation.Token),
                2 => _engine.GetFromToAsync("bucket", 0L, long.MaxValue, observer, _cancellation.Token),
                3 => _engine.GetFromAsync(0L, observer, _cancellation.Token),
                4 => _engine.GetFromToAsync(0L, long.MaxValue, observer, _cancellation.Token),
                5 => _engine.GetStreamsToSnapshotAsync("bucket", 0, Observer<IStreamHead>(), _cancellation.Token),
                _ => _engine.GetDeletedCommitsAsync(observer, _cancellation.Token)
            });
            if (stage == "observer delayed") operation.IsCompleted.Should().BeFalse();
            _resume.TrySetResult(true);
            _escaped = await operation.ConfigureAwait(false);
        }

        [Fact]
        public void Should_translate_only_storage_failures_unless_legacy_behavior_is_requested()
        {
            _escaped.Should().BeNull();
            if (stage == "early stop")
            {
                _reported.Should().BeNull();
                return;
            }
            if (stage == "observer delayed")
                _failure.StackTrace.Should().Contain(nameof(FailAfterResumeAsync));
            if (kind == "other" || (!legacy && (stage == "dispose" || stage == "serializer" || stage.StartsWith("observer")))) _reported.Should().BeSameAs(_failure);
            else
            {
                _reported.Should().BeOfType(kind == "connection" ? typeof(StorageUnavailableException) : typeof(StorageException));
                _reported!.InnerException.Should().BeSameAs(_failure);
            }
        }

        [Fact]
        public void Should_preserve_notification_and_resource_contracts()
        {
            // Driver guards never enclose application work in the default mode.
            if (stage == "find" || stage == "later batch" || (legacy && stage != "early stop"))
                _engine.CallbackFailure.Should().BeSameAs(_failure);
            else
                _engine.CallbackFailure.Should().BeNull();
            _engine.WrapperCalls.Should().Be(legacy ? 1 : stage == "find" ? 1 : stage == "later batch" || stage == "dispose" ? 3 : 2);
            _engine.Token.Should().Be(_cancellation.Token);
            _errors.Should().Be(stage == "early stop" ? 0 : 1);
            _completions.Should().Be(stage == "early stop" ? 1 : 0);
            _nextCalls.Should().Be(stage == "find" || stage == "serializer" ? 0 : 1);
            if (stage == "find") A.CallTo(() => _cursor.Dispose()).MustNotHaveHappened();
            else A.CallTo(() => _cursor.Dispose()).MustHaveHappenedOnceExactly();
            if (stage == "early stop")
                A.CallTo(() => _cursor.MoveNextAsync(_cancellation.Token)).MustHaveHappenedOnceExactly();
        }

        protected override void Cleanup()
        {
            _engine.Dispose();
            _cancellation.Dispose();
        }

        private sealed class ProbeEngine(IMongoDatabase database, IDocumentSerializer serializer, bool legacy)
            : MongoPersistenceEngine(database, serializer, new MongoPersistenceOptions { WrapAsyncApplicationExceptions = legacy })
        {
            public Exception? CallbackFailure { get; private set; }
            public int WrapperCalls { get; private set; }
            public CancellationToken Token { get; private set; }
            protected override Task TryMongoAsync<T>(Func<Task> callbackAsync, CancellationToken cancellationToken, IAsyncObserver<T>? asyncObserver = null)
            {
                WrapperCalls++;
                Token = cancellationToken;
                return base.TryMongoAsync(async () =>
                {
                    try { await callbackAsync().ConfigureAwait(false); }
                    catch (Exception error) { CallbackFailure = error; throw; }
                }, cancellationToken, asyncObserver);
            }
        }
        private static BsonDocument Document(int path, int batch, bool serializedPayload)
        {
            if (path == 7)
                return new BsonDocument
                {
                    { MongoStreamHeadFields.Id, new BsonDocument
                        {
                            { MongoStreamHeadFields.BucketId, "bucket" },
                            { MongoStreamHeadFields.StreamId, "stream" }
                        }
                    },
                    { MongoStreamHeadFields.HeadRevision, batch },
                    { MongoStreamHeadFields.SnapshotRevision, 0 }
                };
            return new BsonDocument
            {
                { MongoCommitFields.CheckpointNumber, (long)batch },
                { MongoCommitFields.BucketId, "bucket" },
                { MongoCommitFields.StreamId, "stream" },
                { MongoCommitFields.StreamRevisionFrom, batch },
                { MongoCommitFields.StreamRevisionTo, batch },
                { MongoCommitFields.CommitSequence, batch },
                { MongoCommitFields.CommitStamp, new BsonDateTime(DateTime.UtcNow) },
                { MongoCommitFields.Headers, new BsonArray() },
                { MongoCommitFields.Events, serializedPayload
                    ? new BsonArray { new BsonDocument(MongoCommitFields.Payload, new BsonBinaryData(new byte[] { 1 })) }
                    : new BsonArray()
                }
            };
        }

    }
    // Application failures must not be misidentified as event-store outages.
    [TestFixtureSource(nameof(Cases))]
    public class when_Issue108_an_async_read_notifies(int path, string stage, bool legacy) : SpecificationBase
    {
        public static IEnumerable<object[]> Cases =>
            from path in Enumerable.Range(0, 7)
            from stage in new[] { "cancel", "active cancellation", "complete failure", "error failure" }
            from legacy in new[] { false, true }
            select new object[] { path, stage, legacy };

        private readonly CancellationTokenSource _cancellation = new();
        private readonly IAsyncCursor<BsonDocument> _cursor = A.Fake<IAsyncCursor<BsonDocument>>();
        private MongoPersistenceEngine _engine = null!;
        private Exception _failure = null!;
        private Exception? _reported;
        private Exception? _escaped;
        private readonly Exception _terminalFailure = new MongoException("terminal callback failure");
        private int _nextCalls;
        private int _errors;
        private int _completions;

        protected override void Context()
        {
            _failure = new OperationCanceledException(_cancellation.Token);
            var database = A.Fake<IMongoDatabase>();
            var collection = A.Fake<IMongoCollection<BsonDocument>>();
            var serializer = A.Fake<IDocumentSerializer>();
            A.CallTo(() => collection.Settings).Returns(new MongoCollectionSettings());
            A.CallTo(() => collection.WithWriteConcern(A<WriteConcern>._)).Returns(collection);
            A.CallTo(() => database.GetCollection<BsonDocument>(A<string>._, A<MongoCollectionSettings>._)).Returns(collection);
            A.CallTo(() => collection.FindAsync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, _cancellation.Token))
                .Returns(Task.FromResult(_cursor));
            int batch = 0;
            A.CallTo(() => _cursor.MoveNextAsync(_cancellation.Token))
                .ReturnsLazily(() => Task.FromResult(++batch == 1));
            A.CallTo(() => _cursor.Current).Returns(new[] { Document(path == 5) });
            _engine = new MongoPersistenceEngine(database, serializer,
                new MongoPersistenceOptions { WrapAsyncApplicationExceptions = legacy });
        }

        private IAsyncObserver<T> Observer<T>()
        {
            var observer = A.Fake<IAsyncObserver<T>>();
            A.CallTo(() => observer.OnNextAsync(A<T>._, _cancellation.Token)).ReturnsLazily(() =>
            {
                _nextCalls++;
                if (stage == "cancel") _cancellation.Cancel();
                if (stage != "complete failure") return Task.FromException<bool>(_failure);

                return Task.FromResult(true);
            });
            A.CallTo(() => observer.OnErrorAsync(A<Exception>._, _cancellation.Token))
                .ReturnsLazily((Exception error, CancellationToken token) =>
                {
                    _reported = error;
                    _errors++;
                    return stage == "error failure" ? Task.FromException(_terminalFailure) : Task.CompletedTask;
                });
            A.CallTo(() => observer.OnCompletedAsync(_cancellation.Token)).ReturnsLazily(() =>
            {
                _completions++;
                return stage == "complete failure" ? Task.FromException(_terminalFailure) : Task.CompletedTask;
            });
            return observer;
        }

        protected override async Task BecauseAsync()
        {
            var observer = Observer<ICommit>();
            var operation = Catch.ExceptionAsync(() => path switch
            {
                0 => _engine.GetFromAsync("bucket", "stream", 0, int.MaxValue, observer, _cancellation.Token),
                1 => _engine.GetFromAsync("bucket", 0L, observer, _cancellation.Token),
                2 => _engine.GetFromToAsync("bucket", 0L, long.MaxValue, observer, _cancellation.Token),
                3 => _engine.GetFromAsync(0L, observer, _cancellation.Token),
                4 => _engine.GetFromToAsync(0L, long.MaxValue, observer, _cancellation.Token),
                5 => _engine.GetStreamsToSnapshotAsync("bucket", 0, Observer<IStreamHead>(), _cancellation.Token),
                _ => _engine.GetDeletedCommitsAsync(observer, _cancellation.Token)
            });
            _escaped = await operation.ConfigureAwait(false);
        }

        [Fact]
        public void Should_notify_once_without_reclassifying_terminal_failures()
        {
            bool error = stage == "active cancellation" || stage == "error failure";
            _errors.Should().Be(error ? 1 : 0);
            _completions.Should().Be(error ? 0 : 1);
            if (error) _reported.Should().BeSameAs(_failure);
            else _reported.Should().BeNull();
            if (stage.EndsWith("failure")) _escaped.Should().BeSameAs(_terminalFailure);
            else _escaped.Should().BeNull();
            _nextCalls.Should().Be(1);
            A.CallTo(() => _cursor.Dispose()).MustHaveHappenedOnceExactly();
        }

        protected override void Cleanup()
        {
            _engine.Dispose();
            _cancellation.Dispose();
        }

        private static BsonDocument Document(bool streamHead)
        {
            if (streamHead)
                return new BsonDocument
                {
                    { MongoStreamHeadFields.Id, new BsonDocument
                        {
                            { MongoStreamHeadFields.BucketId, "bucket" },
                            { MongoStreamHeadFields.StreamId, "stream" }
                        }
                    },
                    { MongoStreamHeadFields.HeadRevision, 1 },
                    { MongoStreamHeadFields.SnapshotRevision, 0 }
                };
            return new BsonDocument
            {
                { MongoCommitFields.CheckpointNumber, (long)1 },
                { MongoCommitFields.BucketId, "bucket" },
                { MongoCommitFields.StreamId, "stream" },
                { MongoCommitFields.StreamRevisionFrom, 1 },
                { MongoCommitFields.StreamRevisionTo, 1 },
                { MongoCommitFields.CommitSequence, 1 },
                { MongoCommitFields.CommitStamp, new BsonDateTime(DateTime.UtcNow) },
                { MongoCommitFields.Headers, new BsonArray() },
                { MongoCommitFields.Events, new BsonArray() }
            };
        }

    }
}
#endif
