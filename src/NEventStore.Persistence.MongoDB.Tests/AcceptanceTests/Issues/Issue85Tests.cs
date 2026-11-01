using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;

#if MSTEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public abstract class Issue85InitializationConcern : SpecificationBase
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
        protected IMongoDatabase _database = null!;
        protected InitializationProbe _engine = null!;

        protected override void Context()
        {
            var options = new MongoPersistenceOptions();
            var builder = new MongoUrlBuilder(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
            {
                DatabaseName = $"issue85-{Guid.NewGuid():N}"
            };
            _database = options.ConnectToDatabase(builder.ToString());
            _engine = new InitializationProbe(_database, options);
        }

        protected ConcurrentResult RunConcurrentAttempt(bool fail, bool recurse)
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var result = new ConcurrentResult();
            _engine.BeforeInitialize = () =>
            {
                // Only the original attempt is gated. An accidental second attempt is observable.
                if (_engine.Attempts != 1)
                {
                    return;
                }
                if (recurse)
                {
                    result.RecursiveError = Catch.Exception(_engine.Initialize);
                }
                entered.Set();
                if (!release.Wait(Timeout))
                {
                    throw new TimeoutException("The initialization test did not release its gate.");
                }
                if (fail)
                {
                    throw new InvalidOperationException("Injected initialization failure.");
                }
            };
            var first = new Thread(() => result.FirstError = Catch.Exception(_engine.Initialize)) { IsBackground = true };
            var second = new Thread(() => result.SecondError = Catch.Exception(_engine.Initialize)) { IsBackground = true };
            first.Start();
            try
            {
                entered.Wait(Timeout).Should().BeTrue("the first caller must enter initialization");
                second.Start();
                SpinWait.SpinUntil(() =>
                    (second.ThreadState & (ThreadState.WaitSleepJoin | ThreadState.Stopped)) != 0,
                    Timeout).Should().BeTrue("the second caller must either wait or return");
                result.WaiterWasBlocked = (second.ThreadState & ThreadState.WaitSleepJoin) != 0;
            }
            finally
            {
                release.Set();
                first.Join(Timeout).Should().BeTrue("the original attempt must finish");
                if ((second.ThreadState & ThreadState.Unstarted) == 0)
                {
                    second.Join(Timeout).Should().BeTrue("the waiting caller must finish");
                }
            }
            return result;
        }

        protected void AssertAllIndexesExist()
        {
            ReadIndexCounts().Should().Equal(7, 2, 3);
        }

        protected int[] ReadIndexCounts()
        {
            return new[]
            {
                _database.GetCollection<BsonDocument>("Commits").Indexes.List().ToList().Count,
                _database.GetCollection<BsonDocument>("Snapshots").Indexes.List().ToList().Count,
                _database.GetCollection<BsonDocument>("Streams").Indexes.List().ToList().Count
            };
        }

        protected override void Cleanup()
        {
            _engine?.Dispose();
            if (_database != null)
            {
                _database.Client.DropDatabase(_database.DatabaseNamespace.DatabaseName);
            }
        }

        protected sealed class ConcurrentResult
        {
            public bool WaiterWasBlocked { get; set; }
            public Exception? FirstError { get; set; }
            public Exception? SecondError { get; set; }
            public Exception? RecursiveError { get; set; }
        }

        protected sealed class InitializationProbe(IMongoDatabase database, MongoPersistenceOptions options)
            : MongoPersistenceEngine(database, new DocumentObjectSerializer(), options)
        {
            private int _attempts;
            public int Attempts => Volatile.Read(ref _attempts);
            public Action? BeforeInitialize { get; set; }

            protected override void TryMongo(Action callback)
            {
                Interlocked.Increment(ref _attempts);
                BeforeInitialize?.Invoke();
                base.TryMongo(callback);
            }
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue_85_index_creation_retry : Issue85InitializationConcern
    {
        private Exception? _firstError;

        protected override void Context()
        {
            base.Context();
            var commits = _database.GetCollection<BsonDocument>("Commits");
            commits.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("conflictingField"),
                new CreateIndexOptions { Name = MongoCommitIndexes.GetFromCheckpoint }));
            _firstError = Catch.Exception(_engine.Initialize);
            commits.Indexes.DropOne(MongoCommitIndexes.GetFromCheckpoint);
        }

        protected override void Because()
        {
            _engine.Initialize();
        }

        [Fact]
        public void A_failed_index_creation_can_be_retried_after_the_conflict_is_removed()
        {
            _firstError.Should().BeOfType<StorageException>();
            _engine.Attempts.Should().Be(2, "a failed attempt must not mark storage initialized");
            AssertAllIndexesExist();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue_85_checkpoint_generator_retry : Issue85InitializationConcern
    {
        private Exception? _firstError;
        private int[] _indexesAfterFailure = null!;

        protected override void Context()
        {
            base.Context();
            var commits = _database.GetCollection<BsonDocument>("Commits");
            commits.InsertOne(new BsonDocument(MongoCommitFields.CheckpointNumber, "not a checkpoint"));
            _firstError = Catch.Exception(_engine.Initialize);
            _indexesAfterFailure = ReadIndexCounts();
            commits.DeleteMany(Builders<BsonDocument>.Filter.Empty);
        }

        protected override void Because()
        {
            _engine.Initialize();
        }

        [Fact]
        public void A_failed_checkpoint_generator_construction_can_be_retried_after_indexes_succeed()
        {
            _firstError.Should().BeOfType<InvalidCastException>();
            _indexesAfterFailure.Should().Equal(7, 2, 3);
            _engine.Attempts.Should().Be(2, "index creation alone does not finish initialization");
            AssertAllIndexesExist();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue_85_successful_initialization : Issue85InitializationConcern
    {
        protected override void Context()
        {
            base.Context();
            _engine.Initialize();
        }

        protected override void Because()
        {
            _engine.Initialize();
            _engine.Initialize();
        }

        [Fact]
        public void Successful_initialization_is_idempotent()
        {
            _engine.Attempts.Should().Be(1);
            AssertAllIndexesExist();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue_85_concurrent_success : Issue85InitializationConcern
    {
        private ConcurrentResult _result = null!;

        protected override void Because()
        {
            _result = RunConcurrentAttempt(fail: false, recurse: false);
        }

        [Fact]
        public void Concurrent_callers_wait_for_the_same_successful_attempt()
        {
            _result.WaiterWasBlocked.Should().BeTrue("storage is not ready while initialization is still running");
            _result.FirstError.Should().BeNull();
            _result.SecondError.Should().BeNull();
            _engine.Attempts.Should().Be(1);
            AssertAllIndexesExist();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue_85_concurrent_failure : Issue85InitializationConcern
    {
        private ConcurrentResult _result = null!;
        private int _attemptsAfterFailure;

        protected override void Context()
        {
            base.Context();
            _result = RunConcurrentAttempt(fail: true, recurse: false);
            _attemptsAfterFailure = _engine.Attempts;
            _engine.BeforeInitialize = null;
        }

        protected override void Because()
        {
            _engine.Initialize();
        }

        [Fact]
        public void Concurrent_callers_observe_the_same_failure_and_a_later_call_retries()
        {
            _result.WaiterWasBlocked.Should().BeTrue();
            _result.FirstError.Should().BeOfType<InvalidOperationException>();
            _result.SecondError.Should().BeSameAs(_result.FirstError, "waiters must not silently retry a failed attempt");
            _attemptsAfterFailure.Should().Be(1);
            _engine.Attempts.Should().Be(2);
            AssertAllIndexesExist();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue_85_recursive_initialization : Issue85InitializationConcern
    {
        private ConcurrentResult _result = null!;

        protected override void Context()
        {
            base.Context();
            _result = RunConcurrentAttempt(fail: false, recurse: true);
        }

        protected override void Because()
        {
            _engine.Initialize();
        }

        [Fact]
        public void A_caught_recursive_call_does_not_replace_the_in_progress_attempt()
        {
            _result.RecursiveError.Should().BeOfType<InvalidOperationException>("recursive initialization must fail rather than deadlock or report premature success");
            _result.WaiterWasBlocked.Should().BeTrue();
            _result.FirstError.Should().BeNull();
            _result.SecondError.Should().BeNull();
            _engine.Attempts.Should().Be(1, "a recursive caller must not discard the outer caller's attempt");
        }
    }
}
