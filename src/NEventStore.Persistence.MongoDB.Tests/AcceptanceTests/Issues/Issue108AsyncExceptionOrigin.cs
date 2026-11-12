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
            from stage in new[] { "serializer", "observer throw", "observer task", "observer delayed", "early stop", "find", "later batch" }
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
            if (kind == "other" || (!legacy && (stage == "serializer" || stage.StartsWith("observer")))) _reported.Should().BeSameAs(_failure);
            else
            {
                _reported.Should().BeOfType(kind == "connection" ? typeof(StorageUnavailableException) : typeof(StorageException));
                _reported!.InnerException.Should().BeSameAs(_failure);
            }
        }

        [Fact]
        public void Should_preserve_notification_and_resource_contracts()
        {
            // Custom guards observe the origin marker unless legacy behavior is enabled.
            if (!legacy && kind != "other" && (stage == "serializer" || stage.StartsWith("observer")))
                _engine.CallbackFailure!.InnerException.Should().BeSameAs(_failure);
            else if (stage == "early stop")
                _engine.CallbackFailure.Should().BeNull();
            else
                _engine.CallbackFailure.Should().BeSameAs(_failure);
            _engine.WrapperCalls.Should().Be(1);
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
}
#endif
