#if NUNIT
using System.Reflection;
using FakeItEasy;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
using NUnit.Framework;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    [TestFixtureSource(nameof(Cases))]
    public class when_Issue108_an_observer_task_can_be_reused(bool streamHead, bool legacy, string state) : SpecificationBase
    {
        public static IEnumerable<object[]> Cases =>
            from head in new[] { false, true }
            from legacy in new[] { false, true }
            from state in new[] { "true", "false", "pending", "faulted", "canceled", "delayed true", "delayed false" }
            where legacy || state == "true" || state == "false" || state.StartsWith("delayed")
            select new object[] { head, legacy, state };

        private MongoPersistenceEngine _engine = null!;
        private Task<bool> _source = null!;
        private Task<bool> _actual = null!;
        private TaskCompletionSource<bool> _completion = null!;
        private bool _pendingBeforeResume;
        private bool _result;
        private object _observer = null!;
        private BsonDocument _document = null!;

        protected override void Context()
        {
            var database = A.Fake<IMongoDatabase>();
            var collection = A.Fake<IMongoCollection<BsonDocument>>();
            A.CallTo(() => collection.Settings).Returns(new MongoCollectionSettings());
            A.CallTo(() => collection.WithWriteConcern(A<WriteConcern>._)).Returns(collection);
            A.CallTo(() => database.GetCollection<BsonDocument>(A<string>._, A<MongoCollectionSettings>._)).Returns(collection);
            _engine = new MongoPersistenceEngine(database, A.Fake<IDocumentSerializer>(),
                new MongoPersistenceOptions { WrapAsyncApplicationExceptions = legacy });
            var completion = _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (state == "true" || state == "false") completion.SetResult(state == "true");
            if (state == "faulted") completion.SetException(new MongoException("application failure"));
            if (state == "canceled") completion.SetCanceled();
            _source = completion.Task;
            var commitObserver = A.Fake<IAsyncObserver<ICommit>>();
            var headObserver = A.Fake<IAsyncObserver<IStreamHead>>();
            A.CallTo(() => commitObserver.OnNextAsync(A<ICommit>._, CancellationToken.None)).Returns(_source);
            A.CallTo(() => headObserver.OnNextAsync(A<IStreamHead>._, CancellationToken.None)).Returns(_source);
            _observer = streamHead ? (object)headObserver : commitObserver;
            _document = (BsonDocument)typeof(when_Issue108_an_async_read_fails)
                .GetMethod("Document", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { streamHead ? 7 : 0, 1, false })!;
        }

        protected override async Task BecauseAsync()
        {
            // The public read task also owns cursor lifetime; inspect the per-item boundary.
            _actual = (Task<bool>)typeof(MongoPersistenceEngine)
                .GetMethod(streamHead ? "ObserveStreamHeadAsync" : "ObserveCommitAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(_engine, new object[] { _document, _observer, CancellationToken.None })!;
            if (state.StartsWith("delayed"))
            {
                _pendingBeforeResume = !_actual.IsCompleted;
                _completion.SetResult(state == "delayed true");
                _result = await _actual.ConfigureAwait(false);
            }
        }

        [Fact]
        public void Should_reuse_tasks_when_possible_and_preserve_delayed_results()
        {
            if (!state.StartsWith("delayed") || legacy) _actual.Should().BeSameAs(_source);
            if (state.StartsWith("delayed"))
            {
                _pendingBeforeResume.Should().BeTrue();
                _result.Should().Be(state == "delayed true");
            }
        }

        protected override void Cleanup()
        {
            _ = _source.Exception;
            _ = _actual.Exception;
            _engine.Dispose();
        }
    }
}
#endif
