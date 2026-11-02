using FakeItEasy;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Persistence.MongoDB.Support;
using NEventStore.Serialization;
#if NUNIT
using NUnit.Framework;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues.Async
{
    // Paths: default NextAsync, recycle cleanup, in-memory duplicate-ID recovery.
    // Outcomes: checkpoint, empty collection, canceled find, canceled cursor, failed cursor.
    [TestFixture(0, 0)] [TestFixture(0, 1)] [TestFixture(0, 2)] [TestFixture(0, 3)] [TestFixture(0, 4)]
    [TestFixture(1, 0)] [TestFixture(1, 1)] [TestFixture(1, 2)] [TestFixture(1, 3)] [TestFixture(1, 4)]
    [TestFixture(2, 0)] [TestFixture(2, 1)] [TestFixture(2, 2)] [TestFixture(2, 3)] [TestFixture(2, 4)]
    public class when_Issue88_async_checkpoint_reads_are_pending(int path, int outcome) : SpecificationBase
    {
        private readonly IMongoCollection<BsonDocument> _collection = A.Fake<IMongoCollection<BsonDocument>>();
        private readonly IAsyncCursor<BsonDocument> _cursor = A.Fake<IAsyncCursor<BsonDocument>>();
        private readonly CancellationTokenSource _cancellation = new();
        private readonly TaskCompletionSource<IAsyncCursor<BsonDocument>> _find = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Exception _failure = new InvalidOperationException("Injected cursor failure.");
        private InMemoryCheckpointGenerator _generator = null!;
        private MongoPersistenceEngine _engine = null!;
        private FindOptions<BsonDocument, BsonDocument> _options = null!;
        private FilterDefinition<BsonDocument>? _deleteFilter;
        private Task _operation = null!;
        private Exception? _error;
        private bool _pending;
        private long _next;

        protected override void Context()
        {
            // Construction remains synchronous; reject every sync find after that boundary.
            var initial = A.Fake<IAsyncCursor<BsonDocument>>();
            A.CallTo(() => initial.MoveNext(A<CancellationToken>._)).Returns(true);
            A.CallTo(() => initial.Current).Returns(new[] { new BsonDocument(MongoCommitFields.CheckpointNumber, 7L) });
            A.CallTo(() => _collection.FindSync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, A<CancellationToken>._)).Returns(initial);
            _generator = path == 0 ? new AlwaysQueryDbForNextValueCheckpointGenerator(_collection) : new InMemoryCheckpointGenerator(_collection);
            A.CallTo(() => _collection.FindSync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, A<CancellationToken>._))
                .Throws(new InvalidOperationException("Async checkpoint read invoked FindSync."));
            A.CallTo(() => _collection.FindAsync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, _cancellation.Token))
                .Invokes((FilterDefinition<BsonDocument> filter, FindOptions<BsonDocument, BsonDocument> options, CancellationToken token) => _options = options)
                .Returns(_find.Task);
            A.CallTo(() => _cursor.Current).Returns(outcome == 1 ? Array.Empty<BsonDocument>() : new[] { new BsonDocument(MongoCommitFields.CheckpointNumber, 42L) });
            A.CallTo(() => _cursor.MoveNextAsync(_cancellation.Token)).ReturnsLazily(() =>
                outcome == 3 ? Task.FromCanceled<bool>(_cancellation.Token) :
                outcome == 4 ? Task.FromException<bool>(_failure) : Task.FromResult(outcome != 1));
            A.CallTo(() => _collection.DeleteManyAsync(A<FilterDefinition<BsonDocument>>._, A<DeleteOptions>._, _cancellation.Token))
                .Invokes((FilterDefinition<BsonDocument> filter, DeleteOptions options, CancellationToken token) => _deleteFilter = filter)
                .Returns(Task.FromResult<DeleteResult>(new DeleteResult.Acknowledged(0)));
            var database = A.Fake<IMongoDatabase>();
            A.CallTo(() => database.GetCollection<BsonDocument>(A<string>._, A<MongoCollectionSettings>._)).Returns(_collection);
            A.CallTo(() => _collection.WithWriteConcern(A<WriteConcern>._)).Returns(_collection);
            _engine = new MongoPersistenceEngine(database, new DocumentObjectSerializer(), new MongoPersistenceOptions());
        }

        protected override async Task BecauseAsync()
        {
            _operation = path == 0 ? _generator.NextAsync(_cancellation.Token) :
                path == 1 ? _engine.EmptyRecycleBinAsync(_cancellation.Token) :
                _generator.SignalDuplicateIdAsync(8, _cancellation.Token);
            _pending = !_operation.IsCompleted;
            if (outcome == 2 || outcome == 3) _cancellation.Cancel();
            if (outcome == 2) _find.SetException(new OperationCanceledException(_cancellation.Token));
            else _find.SetResult(_cursor);
            _error = await Catch.ExceptionAsync(() => _operation).ConfigureAwait(false);
            if (_error == null && path != 1)
                _next = path == 0 ? await ((Task<long>)_operation).ConfigureAwait(false) : _generator.Next();
        }

        [Fact]
        public void Should_return_before_cursor_acquisition_completes()
        {
            _pending.Should().BeTrue("async callers must not perform synchronous database I/O");
            A.CallTo(() => _collection.FindAsync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, _cancellation.Token)).MustHaveHappenedOnceExactly();
            _options.Limit.Should().Be(1);
            var registry = BsonSerializer.SerializerRegistry;
            var args = new RenderArgs<BsonDocument>(registry.GetSerializer<BsonDocument>(), registry);
            _options.Sort.Render(args).Equals(new BsonDocument(MongoCommitFields.CheckpointNumber, -1)).Should().BeTrue();
            _options.Projection.Render(args).Document[MongoCommitFields.CheckpointNumber].AsInt32.Should().Be(1);
        }

        [Fact]
        public void Should_preserve_checkpoint_and_cleanup_boundaries()
        {
            if (outcome < 2)
            {
                _error.Should().BeNull();
                if (path != 1) _next.Should().Be(outcome == 1 ? 1L : 43L);
                else
                {
                    var registry = BsonSerializer.SerializerRegistry;
                    _deleteFilter!.Render(new RenderArgs<BsonDocument>(registry.GetSerializer<BsonDocument>(), registry))
                        .Equals(new BsonDocument
                        {
                            { MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin },
                            { MongoCommitFields.CheckpointNumber, new BsonDocument("$lt", outcome == 1 ? 0L : 42L) }
                        }).Should().BeTrue();
                }
            }
            else
            {
                _deleteFilter.Should().BeNull("failed checkpoint reads must not delete recycled commits");
                if (outcome == 4) _error.Should().BeSameAs(_failure);
                else
                {
                    _operation.IsCanceled.Should().BeTrue();
                    _error.Should().BeAssignableTo<OperationCanceledException>();
                    ((OperationCanceledException)_error!).CancellationToken.Should().Be(_cancellation.Token);
                }
            }
        }

        [Fact]
        public void Should_dispose_acquired_cursors_even_when_iteration_fails()
        {
            if (outcome == 2) A.CallTo(() => _cursor.Dispose()).MustNotHaveHappened();
            else
            {
                A.CallTo(() => _cursor.MoveNextAsync(_cancellation.Token)).MustHaveHappenedOnceExactly();
                A.CallTo(() => _cursor.Dispose()).MustHaveHappenedOnceExactly();
            }
        }

        protected override void Cleanup()
        {
            _engine?.Dispose();
            _cancellation.Dispose();
        }
    }
}
#endif
