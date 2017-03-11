using MongoDB.Bson;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.MongoDB.Support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NEventStore.Persistence.AcceptanceTests.BDD;
using FluentAssertions;
#if MSTEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
#if NUNIT
	using NUnit.Framework;	
#endif
#if XUNIT
	using Xunit;
	using Xunit.Should;
#endif

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
#if MSTEST
    [TestClass]
#endif
    public class verify_safe_generator_not_create_hole : PersistenceEngineConcern
    {
        private string _streamId;
        private string _bucketId;
        private CommitAttempt _attempt;

        public verify_safe_generator_not_create_hole()
        {
            var options = new MongoPersistenceOptions();
            options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;

            var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
            var collection = db.GetCollection<BsonDocument>("Commits");

            options.CheckpointGenerator = new AlwaysQueryDbForNextValueCheckpointGenerator(collection);
            PersistenceEngineFixture.Options = options;
        }

        protected override void Context()
        {
            _streamId = Guid.NewGuid().ToString();
            var commit = Persistence.Commit(_attempt = _streamId.BuildAttempt());
            _bucketId = commit.BucketId;
        }

        protected override void Because()
        {
            try
            {
                Persistence.Commit(_streamId.BuildAttempt());
                throw new Exception("Previous message should throw concurrency exception");
            }
            catch (ConcurrencyException ex)
            {
                //do nothing.
            }
            Persistence.Commit(_attempt.BuildNextAttempt());
        }

        [Fact]
        public void holes_are_presents()
        {
            var commits = Persistence.GetFrom(_bucketId, _streamId, int.MinValue, int.MaxValue).ToArray();
            commits.Length.Should().Be(2);
            commits[0].CheckpointToken.Should().Be(1);
            commits[1].CheckpointToken.Should().Be(2);
        }

        protected override void Cleanup()
        {
            PersistenceEngineFixture.Options = null;
            base.Cleanup();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class holes_are_filled_after_concurrency_exception : PersistenceEngineConcern
    {
        private string _streamId;
        private string _bucketId;
        private CommitAttempt _attempt;

        public holes_are_filled_after_concurrency_exception()
        {
            var options = new MongoPersistenceOptions();
            options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.FillHole;
            PersistenceEngineFixture.Options = options;
        }

        protected override void Context()
        {
            _streamId = Guid.NewGuid().ToString();
            var commit = Persistence.Commit(_attempt = _streamId.BuildAttempt());
            _bucketId = commit.BucketId;
        }

        protected override void Because()
        {
            try
            {
                Persistence.Commit(_streamId.BuildAttempt());
                throw new Exception("Previous message should throw concurrency exception");
            }
            catch (ConcurrencyException ex)
            {
                //do nothing.
            }
            Persistence.Commit(_attempt.BuildNextAttempt());
        }

        [Fact]
        public void holes_are_not_presents()
        {
            var commits = Persistence.GetFrom(_bucketId, _streamId, int.MinValue, int.MaxValue).ToArray();
            commits.Length.Should().Be(2);
            commits[0].CheckpointToken.Should().Be(1);
            commits[1].CheckpointToken.Should().Be(3);

            commits = Persistence.GetFrom("system", "system.empty.2", int.MinValue, int.MaxValue).ToArray();
            commits.Length.Should().Be(1);
            commits[0].CheckpointToken.Should().Be(2);
        }

        protected override void Cleanup()
        {
            base.Cleanup();
            PersistenceEngineFixture.Options = null;
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class holes_are_not_filled_as_default_behavior : PersistenceEngineConcern
    {
        private string _streamId;
        private string _bucketId;
        private CommitAttempt _attempt;

        public holes_are_not_filled_as_default_behavior()
        {
            var options = new MongoPersistenceOptions();
            options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;

            var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
            var collection = db.GetCollection<BsonDocument>("Commits");

            options.CheckpointGenerator = new InMemoryCheckpointGenerator(collection);
            PersistenceEngineFixture.Options = options;
        }

        protected override void Context()
        {
            _streamId = Guid.NewGuid().ToString();
            var commit = Persistence.Commit(_attempt = _streamId.BuildAttempt());
            _bucketId = commit.BucketId;
        }

        protected override void Because()
        {
            try
            {
                Persistence.Commit(_streamId.BuildAttempt());
                throw new Exception("Previous message should throw concurrency exception");
            }
            catch (ConcurrencyException ex)
            {
                //do nothing.
            }
            Persistence.Commit(_attempt.BuildNextAttempt());
        }

        [Fact]
        public void holes_are_presents()
        {
            var commits = Persistence.GetFrom(_bucketId, _streamId, int.MinValue, int.MaxValue).ToArray();
            commits.Length.Should().Be(2);
            commits[0].CheckpointToken.Should().Be(1);
            commits[1].CheckpointToken.Should().Be(3);

            commits = Persistence.GetFrom("system", "system.2", int.MinValue, int.MaxValue).ToArray();
            commits.Length.Should().Be(0);
        }

        protected override void Cleanup()
        {
            base.Cleanup();
            PersistenceEngineFixture.Options = null;
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class default_behavior_after_concurrency_exception : PersistenceEngineConcern
    {
        private string _streamId;
        private string _bucketId;
        private CommitAttempt _attempt;

        protected override void Context()
        {
            _streamId = Guid.NewGuid().ToString();
            var commit = Persistence.Commit(_attempt = _streamId.BuildAttempt());
            _bucketId = commit.BucketId;
        }

        protected override void Because()
        {
            try
            {
                Persistence.Commit(_streamId.BuildAttempt());
                throw new Exception("Previous message should throw concurrency exception");
            }
            catch (ConcurrencyException ex)
            {
                //do nothing.
            }
            Persistence.Commit(_attempt.BuildNextAttempt());
        }

        [Fact]
        public void holes_are_not_presents()
        {
            var commits = Persistence.GetFrom(_bucketId, _streamId, int.MinValue, int.MaxValue).ToArray();
            commits.Length.Should().Be(2);
            commits[0].CheckpointToken.Should().Be(1);
            commits[1].CheckpointToken.Should().Be(2);

            commits = Persistence.GetFrom("system", "system.2", int.MinValue, int.MaxValue).ToArray();
            commits.Length.Should().Be(0);
        }

    }
}
