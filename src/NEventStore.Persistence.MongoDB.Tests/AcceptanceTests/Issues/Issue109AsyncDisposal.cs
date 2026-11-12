using FakeItEasy;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
#if NUNIT
using NUnit.Framework;
namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues.Async
{
    [TestFixtureSource(nameof(Cases))]
    public class when_Issue109_disposal_crosses_an_async_read_boundary(int path, string stage) : SpecificationBase
    {
        public static IEnumerable<object[]> Cases()
        {
            for (int path = 0; path < 7; path++)
                foreach (var stage in new[] { "before", "acquisition", "advance", "observer", "stop", "normal", "error handler" })
                    yield return new object[] { path, stage };
        }
        private readonly IAsyncCursor<BsonDocument> _cursor = A.Fake<IAsyncCursor<BsonDocument>>();
        private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private MongoPersistenceEngine _engine = null!;
        private Exception? _reported, _escaped;
        private readonly Exception _terminalFailure = new InvalidOperationException("Terminal callback failed.");
        private int _finds, _moves, _next, _errors, _completed;
        protected override void Context()
        {
            var database = A.Fake<IMongoDatabase>();
            var collection = A.Fake<IMongoCollection<BsonDocument>>();
            A.CallTo(() => collection.Settings).Returns(new MongoCollectionSettings());
            A.CallTo(() => collection.WithWriteConcern(A<WriteConcern>._)).Returns(collection);
            A.CallTo(() => database.GetCollection<BsonDocument>(A<string>._, A<MongoCollectionSettings>._)).Returns(collection);
            A.CallTo(() => collection.FindAsync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, CancellationToken.None)).ReturnsLazily(async () =>
                {
                    _finds++;
                    if (stage == "acquisition") await Pause();
                    return _cursor;
                });
            A.CallTo(() => _cursor.MoveNextAsync(CancellationToken.None)).ReturnsLazily(async () =>
            {
                _moves++;
                if (_moves == 1 && stage == "advance") await Pause();
                return _moves <= 2;
            });
            // Two documents prove disposal is checked at batch boundaries, not per document.
            A.CallTo(() => _cursor.Current).Returns(new[] { Document(path == 5 ? 7 : 0, 1, false), Document(path == 5 ? 7 : 0, 2, false) });
            _engine = new MongoPersistenceEngine(database, A.Fake<IDocumentSerializer>(), new MongoPersistenceOptions());
        }
        private async Task Pause()
        {
            _entered.TrySetResult(true);
            await _resume.Task.ConfigureAwait(false);
        }
        private IAsyncObserver<T> Observer<T>()
        {
            var observer = A.Fake<IAsyncObserver<T>>();
            A.CallTo(() => observer.OnNextAsync(A<T>._, CancellationToken.None)).ReturnsLazily(async () =>
            {
                _next++;
                if (_next == 1 && (stage == "observer" || stage == "stop" || stage == "error handler")) await Pause();
                return stage != "stop";
            });
            A.CallTo(() => observer.OnErrorAsync(A<Exception>._, CancellationToken.None))
                .ReturnsLazily((Exception error, CancellationToken _) => { _reported = error; _errors++; return stage == "error handler" ? Task.FromException(_terminalFailure) : Task.CompletedTask; });
            A.CallTo(() => observer.OnCompletedAsync(CancellationToken.None))
                .ReturnsLazily(() => { _completed++; return Task.CompletedTask; });
            return observer;
        }
        protected override async Task BecauseAsync()
        {
            if (stage == "before") _engine.Dispose();
            var observer = Observer<ICommit>();
            var operation = Catch.ExceptionAsync(() => path switch
            {
                0 => _engine.GetFromAsync("bucket", "stream", 0, int.MaxValue, observer, CancellationToken.None),
                1 => _engine.GetFromAsync("bucket", 0L, observer, CancellationToken.None),
                2 => _engine.GetFromToAsync("bucket", 0L, long.MaxValue, observer, CancellationToken.None),
                3 => _engine.GetFromAsync(0L, observer, CancellationToken.None),
                4 => _engine.GetFromToAsync(0L, long.MaxValue, observer, CancellationToken.None),
                5 => _engine.GetStreamsToSnapshotAsync("bucket", 0, Observer<IStreamHead>(), CancellationToken.None),
                _ => _engine.GetDeletedCommitsAsync(observer, CancellationToken.None)
            });
            if (stage != "before" && stage != "normal")
            {
                var reached = await Task.WhenAny(_entered.Task, operation, Task.Delay(TimeSpan.FromSeconds(10)));
                reached.Should().BeSameAs(_entered.Task);
                operation.IsCompleted.Should().BeFalse();
                _engine.Dispose();
                _resume.TrySetResult(true);
            }
            _escaped = await operation.ConfigureAwait(false);
        }
        [Fact]
        public void Should_preserve_terminal_notification_contract()
        {
            if (stage == "before") _escaped.Should().BeOfType<ObjectDisposedException>();
            else if (stage == "error handler") _escaped.Should().BeSameAs(_terminalFailure);
            else _escaped.Should().BeNull();
            bool error = stage == "acquisition" || stage == "advance" || stage == "observer" || stage == "error handler";
            if (error) _reported.Should().BeOfType<ObjectDisposedException>();
            else _reported.Should().BeNull();
            _errors.Should().Be(error ? 1 : 0);
            _completed.Should().Be(stage == "normal" || stage == "stop" ? 1 : 0);
        }
        [Fact]
        public void Should_stop_at_the_next_batch_boundary_and_release_the_owned_cursor()
        {
            _finds.Should().Be(stage == "before" ? 0 : 1);
            _moves.Should().Be(stage == "before" || stage == "acquisition" ? 0 : stage == "normal" ? 3 : 1);
            _next.Should().Be(stage == "before" || stage == "acquisition" ? 0 : stage == "normal" ? 4 : stage == "stop" ? 1 : 2);
            if (stage == "before") A.CallTo(() => _cursor.Dispose()).MustNotHaveHappened();
            else A.CallTo(() => _cursor.Dispose()).MustHaveHappenedOnceExactly();
        }
        protected override void Cleanup() { _resume.TrySetResult(true); _engine.Dispose(); }
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
