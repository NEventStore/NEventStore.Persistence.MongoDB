#if NUNIT
using System.Net;
using FakeItEasy;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
using NUnit.Framework;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    [TestFixtureSource(nameof(Cases))]
    public class when_Issue107_cleanup_reads_its_checkpoint(bool async, string stage, string outcome) : SpecificationBase
    {
        public static IEnumerable<object[]> Cases
        {
            get
            {
                foreach (var async in new[] { false, true })
                {
                    foreach (var stage in new[] { "find", "cursor" })
                    foreach (var failure in new[] { "mongo", "connection", "other" })
                        yield return new object[] { async, stage, failure };
                    foreach (var outcome in new[] { "disposed", "success", "empty" })
                        yield return new object[] { async, "none", outcome };
                }
                foreach (var stage in new[] { "find", "cursor" })
                foreach (var outcome in new[] { "canceled", "active cancellation" })
                    yield return new object[] { true, stage, outcome };
            }
        }

        private readonly IMongoCollection<BsonDocument> _collection = A.Fake<IMongoCollection<BsonDocument>>();
        private readonly IAsyncCursor<BsonDocument> _cursor = A.Fake<IAsyncCursor<BsonDocument>>();
        private readonly CancellationTokenSource _cancellation = new();
        private CleanupEngine _engine = null!;
        private Exception? _failure;
        private Exception? _error;
        private Task _operation = null!;
        private FilterDefinition<BsonDocument>? _deleteFilter;
        private FindOptions<BsonDocument, BsonDocument>? _findOptions;
        private int _findCalls;
        private int _deleteCalls;

        protected override void Context()
        {
            _failure = outcome switch
            {
                "mongo" => new MongoException("checkpoint failed"),
                "connection" => new MongoConnectionException(
                    new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))), "connection failed"),
                "other" => new InvalidOperationException("checkpoint failed"),
                "canceled" or "active cancellation" => new OperationCanceledException(_cancellation.Token),
                _ => null
            };
            A.CallTo(() => _collection.FindSync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, A<CancellationToken>._))
                .ReturnsLazily((FilterDefinition<BsonDocument> filter, FindOptions<BsonDocument, BsonDocument> options, CancellationToken token) =>
                {
                    async.Should().BeFalse("async cleanup must never perform synchronous checkpoint I/O");
                    return Find(options);
                });
            A.CallTo(() => _collection.FindAsync(A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._, _cancellation.Token))
                .ReturnsLazily((FilterDefinition<BsonDocument> filter, FindOptions<BsonDocument, BsonDocument> options, CancellationToken token) =>
                {
                    try { return Task.FromResult(Find(options)); }
                    catch (Exception error) { return Task.FromException<IAsyncCursor<BsonDocument>>(error); }
                });
            A.CallTo(() => _cursor.Current).Returns(outcome == "empty" ? Array.Empty<BsonDocument>() :
                new[] { new BsonDocument(MongoCommitFields.CheckpointNumber, 42L) });
            A.CallTo(() => _cursor.MoveNext(A<CancellationToken>._)).ReturnsLazily(MoveNext);
            A.CallTo(() => _cursor.MoveNextAsync(_cancellation.Token)).ReturnsLazily(() =>
                stage == "cursor" ? Task.FromException<bool>(_failure!) : Task.FromResult(outcome != "empty"));
            A.CallTo(() => _collection.DeleteMany(A<FilterDefinition<BsonDocument>>._, A<CancellationToken>._))
                .ReturnsLazily((FilterDefinition<BsonDocument> filter, CancellationToken token) =>
                {
                    async.Should().BeFalse("async cleanup must never perform synchronous deletion");
                    return Delete(filter);
                });
            A.CallTo(() => _collection.DeleteManyAsync(A<FilterDefinition<BsonDocument>>._, _cancellation.Token))
                .ReturnsLazily((FilterDefinition<BsonDocument> filter, CancellationToken token) => Task.FromResult(Delete(filter)));
            var database = A.Fake<IMongoDatabase>();
            A.CallTo(() => database.GetCollection<BsonDocument>(A<string>._, A<MongoCollectionSettings>._)).Returns(_collection);
            A.CallTo(() => _collection.WithWriteConcern(A<WriteConcern>._)).Returns(_collection);
            _engine = new CleanupEngine(database);
            if (outcome == "disposed") _engine.Dispose();
            if (outcome == "canceled") _cancellation.Cancel();
        }

        private IAsyncCursor<BsonDocument> Find(FindOptions<BsonDocument, BsonDocument> options)
        {
            _findCalls++;
            _findOptions = options;
            if (stage == "find") throw _failure!;
            return _cursor;
        }

        private bool MoveNext()
        {
            if (stage == "cursor") throw _failure!;
            return outcome != "empty";
        }

        private DeleteResult Delete(FilterDefinition<BsonDocument> filter)
        {
            _deleteCalls++;
            _deleteFilter = filter;
            return new DeleteResult.Acknowledged(0);
        }

        protected override async Task BecauseAsync()
        {
            if (async)
            {
                _operation = _engine.EmptyRecycleBinAsync(_cancellation.Token);
                _error = await Catch.ExceptionAsync(() => _operation).ConfigureAwait(false);
            }
            else _error = Catch.Exception(_engine.EmptyRecycleBin);
        }

        [Fact]
        public void Should_preserve_the_storage_exception_and_cancellation_contract()
        {
            if (outcome == "disposed") _error.Should().BeOfType<ObjectDisposedException>();
            else if (outcome == "mongo" || outcome == "connection")
            {
                _error.Should().BeOfType(outcome == "connection" ? typeof(StorageUnavailableException) : typeof(StorageException));
                _error!.InnerException.Should().BeSameAs(_failure, "driver failures must be translated exactly once");
            }
            else _error.Should().BeSameAs(_failure);
            if (outcome == "canceled" || outcome == "active cancellation")
            {
                _operation.IsCanceled.Should().BeTrue();
                ((OperationCanceledException)_error!).CancellationToken.Should().Be(_cancellation.Token);
            }
        }

        [Fact]
        public void Should_guard_the_entire_operation_once_before_any_collection_access()
        {
            _engine.GuardCalls.Should().Be(1, "retain the existing protected virtual guard hook without nesting");
            _engine.UnguardedAccesses.Should().Be(0);
            if (outcome == "disposed") _engine.CollectionAccesses.Should().Be(0);
            _findCalls.Should().Be(outcome == "disposed" ? 0 : 1);
        }

        [Fact]
        public void Should_never_delete_after_a_failed_read_and_preserve_the_cutoff_on_success()
        {
            var succeeds = outcome == "success" || outcome == "empty";
            _deleteCalls.Should().Be(succeeds ? 1 : 0);
            if (!succeeds) return;
            var registry = BsonSerializer.SerializerRegistry;
            var args = new RenderArgs<BsonDocument>(registry.GetSerializer<BsonDocument>(), registry);
            _findOptions!.Limit.Should().Be(1);
            _findOptions.Sort.Render(args).Equals(new BsonDocument(MongoCommitFields.CheckpointNumber, -1)).Should().BeTrue();
            _findOptions.Projection.Render(args).Document[MongoCommitFields.CheckpointNumber].AsInt32.Should().Be(1);
            _deleteFilter!.Render(args).Equals(new BsonDocument
            {
                { MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin },
                { MongoCommitFields.CheckpointNumber, new BsonDocument("$lt", outcome == "empty" ? 0L : 42L) }
            }).Should().BeTrue();
        }

        [Fact]
        public void Should_dispose_every_acquired_cursor_and_forward_the_request_token()
        {
            if (stage == "find" || outcome == "disposed")
                A.CallTo(() => _cursor.Dispose()).MustNotHaveHappened();
            else
            {
                A.CallTo(() => _cursor.Dispose()).MustHaveHappenedOnceExactly();
                if (async) A.CallTo(() => _cursor.MoveNextAsync(_cancellation.Token)).MustHaveHappenedOnceExactly();
                else A.CallTo(() => _cursor.MoveNext(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            }
            if (async && outcome != "disposed")
                A.CallTo(() => _collection.FindAsync(A<FilterDefinition<BsonDocument>>._,
                    A<FindOptions<BsonDocument, BsonDocument>>._, _cancellation.Token)).MustHaveHappenedOnceExactly();
        }

        protected override void Cleanup()
        {
            _engine?.Dispose();
            _cancellation.Dispose();
        }

        private sealed class CleanupEngine(IMongoDatabase database)
            : MongoPersistenceEngine(database, new DocumentObjectSerializer(), new MongoPersistenceOptions())
        {
            public int CollectionAccesses { get; private set; }
            public int UnguardedAccesses { get; private set; }
            public int GuardCalls { get; private set; }
            private bool _guarded;

            protected override IMongoCollection<BsonDocument> PersistedCommits
            {
                get
                {
                    CollectionAccesses++;
                    if (!_guarded) UnguardedAccesses++;
                    return base.PersistedCommits;
                }
            }

            protected override void TryMongo(Action callback)
            {
                GuardCalls++;
                _guarded = true;
                try { base.TryMongo(callback); }
                finally { _guarded = false; }
            }

            protected override async Task TryMongoAsync<T>(Func<Task> callbackAsync, CancellationToken cancellationToken, IAsyncObserver<T>? asyncObserver = null)
            {
                GuardCalls++;
                _guarded = true;
                try { await base.TryMongoAsync(callbackAsync, cancellationToken, asyncObserver).ConfigureAwait(false); }
                finally { _guarded = false; }
            }
        }
    }
}
#endif
