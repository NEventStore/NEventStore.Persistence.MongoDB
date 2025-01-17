using MongoDB.Bson;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.MongoDB.Support;
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

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Async
{
#if MSTEST
    [TestClass]
#endif
    public class Verify_safe_generator_not_create_hole : PersistenceEngineConcern
    {
        private string? _streamId;
        private string? _bucketId;
        private CommitAttempt? _attempt;

        public Verify_safe_generator_not_create_hole()
        {
            var options = new MongoPersistenceOptions
            {
                ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue
            };

            var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
            var collection = db.GetCollection<BsonDocument>("Commits");
            options.CheckpointGenerator = new AlwaysQueryDbForNextValueCheckpointGenerator(collection);

            PersistenceEngineFixture.Options = options;

            // workaround for test initialization to have uniform config for all 3 test frameworks
            // we can't use ClassInitialize, TestFixtureSetup or SetFixture
            Fixture!.Initialize(ConfiguredPageSizeForTesting);

            // reset this immediately, hopefully will not impact other tests
            PersistenceEngineFixture.Options = null;
        }

        protected override async Task ContextAsync()
        {
            _streamId = Guid.NewGuid().ToString();
            _attempt = _streamId.BuildAttempt();
            var commit = await Persistence.CommitAsync(_attempt).ConfigureAwait(false);
            _bucketId = commit!.BucketId;
        }

        protected override async Task BecauseAsync()
        {
            try
            {
                await Persistence.CommitAsync(_streamId!.BuildAttempt()).ConfigureAwait(false);
                throw new Exception("Previous message should throw concurrency exception");
            }
            catch (ConcurrencyException)
            {
                //do nothing.
            }
            await Persistence.CommitAsync(_attempt!.BuildNextAttempt()).ConfigureAwait(false);
        }

        [Fact]
        public async Task Holes_are_presents()
        {
            var observer = new CommitStreamObserver();
            await Persistence.GetFromAsync(_bucketId!, _streamId!, int.MinValue, int.MaxValue, observer).ConfigureAwait(false);
            var commits = observer.Commits.ToArray();
            commits.Length.Should().Be(2);
            commits[0].CheckpointToken.Should().Be(1);
            commits[1].CheckpointToken.Should().Be(2);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Holes_are_filled_after_concurrency_exception : PersistenceEngineConcern
    {
        private string? _streamId;
        private string? _bucketId;
        private CommitAttempt? _attempt;

        public Holes_are_filled_after_concurrency_exception()
        {
            var options = new MongoPersistenceOptions
            {
                ConcurrencyStrategy = ConcurrencyExceptionStrategy.FillHole
            };
            PersistenceEngineFixture.Options = options;

            // workaround for test initialization to have uniform config for all 3 test frameworks
            // we can't use ClassInitialize, TestFixtureSetup or SetFixture
            Fixture!.Initialize(ConfiguredPageSizeForTesting);

            // reset this immediately, hopefully will not impact other tests
            PersistenceEngineFixture.Options = null;
        }

        protected override async Task ContextAsync()
        {
            _streamId = Guid.NewGuid().ToString();
            var commit = await Persistence.CommitAsync(_attempt = _streamId.BuildAttempt()).ConfigureAwait(false);
            _bucketId = commit!.BucketId;
        }

        protected override async Task BecauseAsync()
        {
            try
            {
                await Persistence.CommitAsync(_streamId!.BuildAttempt()).ConfigureAwait(false);
                throw new Exception("Previous message should throw concurrency exception");
            }
            catch (ConcurrencyException)
            {
                //do nothing.
            }
            await Persistence.CommitAsync(_attempt!.BuildNextAttempt()).ConfigureAwait(false);
        }

        [Fact]
        public async Task Holes_are_not_presents()
        {
            var observer = new CommitStreamObserver();
            await Persistence.GetFromAsync(_bucketId!, _streamId!, int.MinValue, int.MaxValue, observer).ConfigureAwait(false);
            var commits = observer.Commits.ToArray();
            commits.Length.Should().Be(2);
            commits[0].CheckpointToken.Should().Be(1);
            commits[1].CheckpointToken.Should().Be(3);

            observer = new CommitStreamObserver();
            await Persistence.GetFromAsync("system", "system.empty.2", int.MinValue, int.MaxValue, observer).ConfigureAwait(false);
            commits = observer.Commits.ToArray();
            commits.Length.Should().Be(1);
            commits[0].CheckpointToken.Should().Be(2);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Holes_are_not_filled_as_default_behavior : PersistenceEngineConcern
    {
        private string? _streamId;
        private string? _bucketId;
        private CommitAttempt? _attempt;

        public Holes_are_not_filled_as_default_behavior()
        {
            var options = new MongoPersistenceOptions
            {
                ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue
            };

            var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
            var collection = db.GetCollection<BsonDocument>("Commits");

            options.CheckpointGenerator = new InMemoryCheckpointGenerator(collection);
            PersistenceEngineFixture.Options = options;

            // workaround for test initialization to have uniform config for all 3 test frameworks
            // we can't use ClassInitialize, TestFixtureSetup or SetFixture
            Fixture!.Initialize(ConfiguredPageSizeForTesting);

            // reset this immediately, hopefully will not impact other tests
            PersistenceEngineFixture.Options = null;
        }

        protected override async Task ContextAsync()
        {
            _streamId = Guid.NewGuid().ToString();
            var commit = await Persistence.CommitAsync(_attempt = _streamId.BuildAttempt()).ConfigureAwait(false);
            _bucketId = commit!.BucketId;
        }

        protected override async Task BecauseAsync()
        {
            try
            {
                await Persistence.CommitAsync(_streamId!.BuildAttempt()).ConfigureAwait(false);
                throw new Exception("Previous message should throw concurrency exception");
            }
            catch (ConcurrencyException)
            {
                //do nothing.
            }
            await Persistence.CommitAsync(_attempt!.BuildNextAttempt()).ConfigureAwait(false);
        }

        [Fact]
        public async Task Holes_are_presents()
        {
            var observer = new CommitStreamObserver();
            await Persistence.GetFromAsync(_bucketId!, _streamId!, int.MinValue, int.MaxValue, observer).ConfigureAwait(false);
            var commits = observer.Commits.ToArray();
            commits.Length.Should().Be(2);
            commits[0].CheckpointToken.Should().Be(1);
            commits[1].CheckpointToken.Should().Be(3);

            observer = new CommitStreamObserver();
            await Persistence.GetFromAsync("system", "system.2", int.MinValue, int.MaxValue, observer).ConfigureAwait(false);
            commits = observer.Commits.ToArray();
            commits.Length.Should().Be(0);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Default_behavior_after_concurrency_exception : PersistenceEngineConcern
    {
        private string? _streamId;
        private string? _bucketId;
        private CommitAttempt? _attempt;

        protected override async Task ContextAsync()
        {
            _streamId = Guid.NewGuid().ToString();
            var commit = await Persistence.CommitAsync(_attempt = _streamId.BuildAttempt()).ConfigureAwait(false);
            _bucketId = commit!.BucketId;
        }

        protected override async Task BecauseAsync()
        {
            try
            {
                await Persistence.CommitAsync(_streamId!.BuildAttempt()).ConfigureAwait(false);
                throw new Exception("Previous message should throw concurrency exception");
            }
            catch (ConcurrencyException)
            {
                //do nothing.
            }
            await Persistence.CommitAsync(_attempt!.BuildNextAttempt()).ConfigureAwait(false);
        }

        [Fact]
        public async Task Holes_are_not_presents()
        {
            var observer = new CommitStreamObserver();
            await Persistence.GetFromAsync(_bucketId!, _streamId!, int.MinValue, int.MaxValue, observer).ConfigureAwait(false);
            var commits = observer.Commits.ToArray();
            commits.Length.Should().Be(2);
            commits[0].CheckpointToken.Should().Be(1);
            commits[1].CheckpointToken.Should().Be(2);

            observer = new CommitStreamObserver();
            await Persistence.GetFromAsync("system", "system.2", int.MinValue, int.MaxValue, observer).ConfigureAwait(false);
            commits = observer.Commits.ToArray();
            commits.Length.Should().Be(0);
        }
    }
}
