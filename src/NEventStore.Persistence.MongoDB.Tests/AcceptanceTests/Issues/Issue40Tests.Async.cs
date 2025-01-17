using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using FluentAssertions;
#if MSTEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
#if NUNIT
#endif
#if XUNIT
using Xunit;
using Xunit.Should;
#endif

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues.Async
{
#if MSTEST
    [TestClass]
#endif
    public class Issue40_verify_ability_to_opt_out_stream_head : PersistenceEngineConcern
    {
        private readonly IMongoCollection<BsonDocument> _streamHeads;

        public Issue40_verify_ability_to_opt_out_stream_head()
        {
            var options = new MongoPersistenceOptions
            {
                ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue,
                DisableSnapshotSupport = true
            };

            var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
            _streamHeads = db.GetCollection<BsonDocument>("Streams");

            PersistenceEngineFixture.Options = options;

            // workaround for test initialization to have uniform config for all 3 test frameworks
            // we can't use ClassInitialize, TestFixtureSetup or SetFixture
            Fixture!.Initialize(ConfiguredPageSizeForTesting);

            // reset this immediately, hopefully will not impact other tests
            PersistenceEngineFixture.Options = null;
        }

        protected override void Context()
        {
        }

        protected override Task BecauseAsync()
        {
            var streamId = Guid.NewGuid().ToString();
            CommitAttempt attempt = streamId.BuildAttempt();
            return Persistence.CommitAsync(attempt);
        }

        [Fact]
        public void No_stream_heads_are_saved()
        {
            var heads = _streamHeads.Find(Builders<BsonDocument>.Filter.Empty);
            heads.CountDocuments().Should().Be(0);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue40_calling_AddSnapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
    {
        public Issue40_calling_AddSnapshot_function_when_snapshot_disabled_throws()
        {
            var options = new MongoPersistenceOptions
            {
                ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue,
                DisableSnapshotSupport = true
            };

            PersistenceEngineFixture.Options = options;

            // workaround for test initialization to have uniform config for all 3 test frameworks
            // we can't use ClassInitialize, TestFixtureSetup or SetFixture
            Fixture!.Initialize(ConfiguredPageSizeForTesting);

            // reset this immediately, hopefully will not impact other tests
            PersistenceEngineFixture.Options = null;
        }

        protected override void Context()
        {
        }

        private Exception? _ex;

        protected override async Task BecauseAsync()
        {
            var streamId = Guid.NewGuid().ToString();
            CommitAttempt attempt = streamId.BuildAttempt();
            await Persistence.CommitAsync(streamId.BuildAttempt()).ConfigureAwait(false);

            _ex = await Catch.ExceptionAsync(() => Persistence.AddSnapshotAsync(new Snapshot(streamId, 1, new object()))).ConfigureAwait(false);
        }

        [Fact]
        public void Exception_was_thrown()
        {
            _ex.Should().BeOfType<NotSupportedException>();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue40_calling_GetSnapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
    {
        public Issue40_calling_GetSnapshot_function_when_snapshot_disabled_throws()
        {
            var options = new MongoPersistenceOptions
            {
                ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue,
                DisableSnapshotSupport = true
            };

            PersistenceEngineFixture.Options = options;

            // workaround for test initialization to have uniform config for all 3 test frameworks
            // we can't use ClassInitialize, TestFixtureSetup or SetFixture
            Fixture!.Initialize(ConfiguredPageSizeForTesting);

            // reset this immediately, hopefully will not impact other tests
            PersistenceEngineFixture.Options = null;
        }

        protected override void Context()
        {
        }

        private Exception? _ex;

        protected override async Task BecauseAsync()
        {
            var streamId = Guid.NewGuid().ToString();
            CommitAttempt attempt = streamId.BuildAttempt();
            await Persistence.CommitAsync(streamId.BuildAttempt()).ConfigureAwait(false);

            _ex = await Catch.ExceptionAsync(() => Persistence.GetSnapshotAsync(streamId, 1)).ConfigureAwait(false);
        }

        [Fact]
        public void Exception_was_thrown()
        {
            _ex.Should().BeOfType<NotSupportedException>();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue40_calling_GetStreamToSnapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
    {
        public Issue40_calling_GetStreamToSnapshot_function_when_snapshot_disabled_throws()
        {
            var options = new MongoPersistenceOptions
            {
                ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue,
                DisableSnapshotSupport = true
            };

            PersistenceEngineFixture.Options = options;

            // workaround for test initialization to have uniform config for all 3 test frameworks
            // we can't use ClassInitialize, TestFixtureSetup or SetFixture
            Fixture!.Initialize(ConfiguredPageSizeForTesting);

            // reset this immediately, hopefully will not impact other tests
            PersistenceEngineFixture.Options = null;
        }

        protected override void Context()
        {
        }

        private Exception? _ex;

        protected override async Task BecauseAsync()
        {
            var streamId = Guid.NewGuid().ToString();
            CommitAttempt attempt = streamId.BuildAttempt();
            await Persistence.CommitAsync(streamId.BuildAttempt()).ConfigureAwait(false);

            _ex = await Catch.ExceptionAsync(() => Persistence.GetStreamsToSnapshotAsync("testBucket", 1, new StreamHeadObserver())).ConfigureAwait(false);
        }

        [Fact]
        public void Exception_was_thrown()
        {
            _ex.Should().BeOfType<NotSupportedException>();
        }
    }
}
