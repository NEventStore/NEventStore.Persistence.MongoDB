#if NUNIT
using System.Net;
using FakeItEasy;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
using NUnit.Framework;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
    [TestFixtureSource(nameof(Cases))]
    public class when_a_synchronous_read_fails : SpecificationBase
    {
        public static IEnumerable<object[]> Cases =>
            from path in Enumerable.Range(0, EnumerationProbe.ReadCount)
            from stage in new[] { "immediate", "find", "first batch", "later batch" }
            from kind in new[] { "mongo", "connection", "other" }
            select new object[] { path, stage, kind };

        private readonly int _path;
        private readonly string _stage;
        private readonly string _kind;
        private EnumerationProbe _probe = null!;
        private Exception _failure = null!;
        private Exception? _creationError;
        private Exception? _enumerationError;
        private int _findCallsBeforeEnumeration;
        private int _itemsRead;

        public when_a_synchronous_read_fails(int path, string stage, string kind)
        {
            _path = path;
            _stage = stage;
            _kind = kind;
        }

        protected override void Context()
        {
            _failure = EnumerationProbe.Failure(_kind);
            _probe = new EnumerationProbe(_path, _stage, _failure);
        }

        protected override void Because()
        {
            IEnumerable<object>? result = null;
            _creationError = Catch.Exception(() => result = _probe.Read());
            _findCallsBeforeEnumeration = _probe.FindCalls;
            if (result != null)
                _enumerationError = Catch.Exception(() =>
                {
                    foreach (var item in result)
                        _itemsRead++;
                });
        }

        [Fact]
        public void Should_translate_only_driver_failures_at_the_original_boundary()
        {
            var error = _stage == "immediate" ? _creationError : _enumerationError;
            if (_kind == "other")
                error.Should().BeSameAs(_failure);
            else
            {
                error.Should().BeOfType(_kind == "connection"
                    ? typeof(StorageUnavailableException) : typeof(StorageException));
                error!.InnerException.Should().BeSameAs(_failure);
            }
            if (_stage != "immediate")
                _creationError.Should().BeNull();
        }

        [Fact]
        public void Should_remain_lazy_and_stream_prior_batches_before_a_later_failure()
        {
            _findCallsBeforeEnumeration.Should().Be(0);
            _itemsRead.Should().Be(_stage == "later batch" ? 1 : 0);
        }

        [Fact]
        public void Should_dispose_every_acquired_cursor_on_failure()
        {
            _probe.Cursors.Count.Should().Be(_stage == "first batch" || _stage == "later batch" ? 1 : 0);
            foreach (var cursor in _probe.Cursors)
                A.CallTo(() => cursor.Dispose()).MustHaveHappenedOnceExactly();
        }

        protected override void Cleanup() => _probe.Engine.Dispose();
    }

    [TestFixtureSource(nameof(Cases))]
    public class when_a_synchronous_read_is_enumerated : SpecificationBase
    {
        public static IEnumerable<object[]> Cases =>
            from path in Enumerable.Range(0, EnumerationProbe.ReadCount)
            from mode in new[] { "complete", "early", "consumer failure", "repeat" }
            select new object[] { path, mode };

        private readonly int _path;
        private readonly string _mode;
        private EnumerationProbe _probe = null!;
        private int _itemsRead;
        private readonly List<long> _positions = [];
        private int _findCallsBeforeEnumeration;
        private Exception? _error;
        private readonly Exception _consumerFailure = new InvalidOperationException("consumer");

        public when_a_synchronous_read_is_enumerated(int path, string mode)
        {
            _path = path;
            _mode = mode;
        }

        protected override void Context() => _probe = new EnumerationProbe(_path);

        protected override void Because()
        {
            var result = _probe.Read();
            _findCallsBeforeEnumeration = _probe.FindCalls;
            _error = Catch.Exception(() =>
            {
                foreach (var item in result)
                {
                    _itemsRead++;
                    _positions.Add(item is ICommit commit ? commit.CheckpointToken : ((IStreamHead)item).HeadRevision);
                    if (_mode == "early")
                        break;
                    if (_mode == "consumer failure")
                        throw _consumerFailure;
                }
                if (_mode == "repeat")
                    _itemsRead += result.Count();
            });
        }

        [Fact]
        public void Should_keep_streaming_and_allow_repeated_enumeration()
        {
            _findCallsBeforeEnumeration.Should().Be(0);
            _itemsRead.Should().Be(_mode == "repeat" ? 4 : _mode == "complete" ? 2 : 1);
            _probe.FindCalls.Should().Be(_mode == "repeat" ? 2 : 1);
            // Early termination must not fetch a second batch, even if it is empty.
            _probe.MoveNextCalls.Should().Be(_mode == "repeat" ? 8 : _mode == "complete" ? 4 : 1);
            _positions.Should().Equal(_mode == "early" || _mode == "consumer failure" ? new long[] { 1 } : new long[] { 1, 3 });
            _error.Should().BeSameAs(_mode == "consumer failure" ? _consumerFailure : null);
        }

        [Fact]
        public void Should_dispose_cursors_on_completion_and_early_termination()
        {
            foreach (var cursor in _probe.Cursors)
                A.CallTo(() => cursor.Dispose()).MustHaveHappenedOnceExactly();
        }

        protected override void Cleanup() => _probe.Engine.Dispose();
    }

    [TestFixtureSource(nameof(Cases))]
    public class when_a_synchronous_commit_serializer_fails : SpecificationBase
    {
        public static IEnumerable<object[]> Cases =>
            from path in Enumerable.Range(0, EnumerationProbe.ReadCount).Where(path => path != 7)
            from kind in new[] { "mongo", "connection", "other" }
            select new object[] { path, kind };

        private readonly int _path;
        private readonly string _kind;
        private EnumerationProbe _probe = null!;
        private Exception _failure = null!;
        private Exception? _error;

        public when_a_synchronous_commit_serializer_fails(int path, string kind)
        {
            _path = path;
            _kind = kind;
        }

        protected override void Context()
        {
            _failure = EnumerationProbe.Failure(_kind);
            _probe = new EnumerationProbe(_path, "serializer", _failure);
        }

        protected override void Because() => _error = Catch.Exception(() => _probe.Read().ToArray());

        [Fact]
        public void Should_preserve_serializer_errors_even_when_they_use_driver_exception_types()
        {
            _error.Should().BeSameAs(_failure);
            _probe.FindCalls.Should().Be(1);
            _probe.MoveNextCalls.Should().Be(1);
            A.CallTo(() => _probe.Cursors.Single().Dispose()).MustHaveHappenedOnceExactly();
        }

        protected override void Cleanup() => _probe.Engine.Dispose();
    }

    [TestFixtureSource(nameof(Cases))]
    public class when_the_engine_is_disposed_during_a_synchronous_read : SpecificationBase
    {
        public static IEnumerable<object[]> Cases =>
            from path in Enumerable.Range(0, EnumerationProbe.ReadCount)
            from stage in new[] { "before call", "before iteration", "between batches" }
            select new object[] { path, stage };

        private readonly int _path;
        private readonly string _stage;
        private EnumerationProbe _probe = null!;
        private Exception? _error;

        public when_the_engine_is_disposed_during_a_synchronous_read(int path, string stage)
        {
            _path = path;
            _stage = stage;
        }

        protected override void Context() => _probe = new EnumerationProbe(_path);

        protected override void Because()
        {
            _error = Catch.Exception(() =>
            {
                if (_stage == "before call")
                    _probe.Engine.Dispose();
                var result = _probe.Read();
                using var iterator = result.GetEnumerator();
                if (_stage == "between batches")
                    iterator.MoveNext();
                _probe.Engine.Dispose();
                iterator.MoveNext();
            });
        }

        [Fact]
        public void Should_guard_driver_calls_and_still_release_acquired_cursors()
        {
            _error.Should().BeOfType<ObjectDisposedException>();
            _probe.FindCalls.Should().Be(_stage == "between batches" ? 1 : 0);
            _probe.MoveNextCalls.Should().Be(_stage == "between batches" ? 1 : 0);
            _probe.Cursors.Count.Should().Be(_stage == "between batches" ? 1 : 0);
            foreach (var cursor in _probe.Cursors)
                A.CallTo(() => cursor.Dispose()).MustHaveHappenedOnceExactly();
        }

        protected override void Cleanup() => _probe.Engine.Dispose();
    }

    internal sealed class EnumerationProbe
    {
        public const int ReadCount = 9;
        private readonly int _path;
        public MongoPersistenceEngine Engine { get; }
        public int FindCalls { get; private set; }
        public int MoveNextCalls { get; private set; }
        public List<IAsyncCursor<BsonDocument>> Cursors { get; } = [];

        public EnumerationProbe(int path, string? failureStage = null, Exception? failure = null)
        {
            _path = path;
            var database = A.Fake<IMongoDatabase>();
            var collection = A.Fake<IMongoCollection<BsonDocument>>();
            var serializer = A.Fake<IDocumentSerializer>();
            A.CallTo(() => collection.Settings).Returns(new MongoCollectionSettings());
            A.CallTo(() => collection.WithWriteConcern(A<WriteConcern>._)).Returns(collection);
            A.CallTo(() => database.GetCollection<BsonDocument>(A<string>._, A<MongoCollectionSettings>._))
                .Returns(collection);
            A.CallTo(() => serializer.Deserialize<EventMessage>(A<object>._)).Throws(failure ?? new Exception("unexpected serializer call"));
            A.CallTo(() => collection.FindSync(
                A<FilterDefinition<BsonDocument>>._,
                A<FindOptions<BsonDocument, BsonDocument>>._,
                A<CancellationToken>._)).ReturnsLazily(() =>
            {
                FindCalls++;
                if (failureStage == "find")
                    throw failure!;
                var cursor = A.Fake<IAsyncCursor<BsonDocument>>();
                Cursors.Add(cursor);
                int batch = 0;
                A.CallTo(() => cursor.MoveNext(A<CancellationToken>._)).ReturnsLazily(() =>
                {
                    MoveNextCalls++;
                    batch++;
                    if (failureStage == "first batch" || (failureStage == "later batch" && batch == 2))
                        throw failure!;
                    return batch <= 3;
                });
                A.CallTo(() => cursor.Current).ReturnsLazily(() => batch == 2
                    ? Array.Empty<BsonDocument>()
                    : new[] { Document(path, batch, failureStage == "serializer") });
                return cursor;
            });
            Engine = new ProbeEngine(database, serializer, collection, failureStage == "immediate" ? failure : null);
        }

        public IEnumerable<object> Read()
        {
#pragma warning disable CS0618 // Date-based public reads are still supported.
            return _path switch
            {
                0 => Engine.GetFrom("bucket", "stream", 0, int.MaxValue),
                1 => Engine.GetFrom("bucket", DateTime.MinValue),
                2 => Engine.GetFrom("bucket", 0L),
                3 => Engine.GetFromTo("bucket", 0L, long.MaxValue),
                4 => Engine.GetFrom(0L),
                5 => Engine.GetFromTo(0L, long.MaxValue),
                6 => Engine.GetFromTo("bucket", DateTime.MinValue, DateTime.MaxValue),
                7 => Engine.GetStreamsToSnapshot("bucket", 0),
                8 => Engine.GetDeletedCommits(),
                _ => throw new ArgumentOutOfRangeException(nameof(_path))
            };
#pragma warning restore CS0618
        }

        public static Exception Failure(string kind) => kind switch
        {
            "connection" => new MongoConnectionException(
                new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017))), "connection failed"),
            "mongo" => new MongoException("driver failed"),
            _ => new FormatException("not a driver failure")
        };

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

        private sealed class ProbeEngine : MongoPersistenceEngine
        {
            private readonly IMongoCollection<BsonDocument> _collection;
            private readonly Exception? _failure;

            public ProbeEngine(IMongoDatabase database, IDocumentSerializer serializer,
                IMongoCollection<BsonDocument> collection, Exception? failure)
                : base(database, serializer, new MongoPersistenceOptions())
            {
                _collection = collection;
                _failure = failure;
            }

            protected override IMongoCollection<BsonDocument> PersistedCommits =>
                _failure == null ? _collection : throw _failure;

            protected override IMongoCollection<BsonDocument> PersistedStreamHeads =>
                _failure == null ? _collection : throw _failure;
        }
    }
}
#endif
