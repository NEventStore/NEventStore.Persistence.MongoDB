namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
    using System;
    using NEventStore.Persistence.AcceptanceTests;
    using NEventStore.Persistence.AcceptanceTests.BDD;
    using FluentAssertions;
#if MSTEST
    using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
#if NUNIT
    using NUnit.Framework;
    using global::MongoDB.Driver;
    using ZstdSharp.Unsafe;
    using System.Linq;
#endif
#if XUNIT
    using Xunit;
    using Xunit.Should;
#endif

    /// <summary>
    /// In Order for transactions to be used we need to know the IMongoClinet instance
    /// that was used to connect to the database.
    /// Pass it in <see cref="MongoPersistenceOptions"/> when configuring the persistence engine
    /// (see <see cref="MongoPersistenceWireup"/> and its extensions methods).
    /// </summary>
    public class AbstractTransactionTests : SpecificationBase
    {
        protected IPersistStreams Persistence;
        protected IMongoClient MongoClient;

        protected override void Context()
        {
            var connectionString = AcceptanceTestMongoPersistenceFactory.GetConnectionString();
            MongoClient = new MongoClient(connectionString);
            var mongoPersistenceOptions = new MongoPersistenceOptions(null, MongoClient);

            Persistence = new AcceptanceTestMongoPersistenceFactory(mongoPersistenceOptions).Build();
            Persistence.Initialize();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_a_Transaction_Was_Committed : AbstractTransactionTests
    {
        private ICommit _commit;
        private string _streamId;
        protected override void Because()
        {
            using (var session = MongoClient.StartSession())
            {
                session.StartTransaction();

                _streamId = Guid.NewGuid().ToString();
                _commit = Persistence.Commit(_streamId.BuildAttempt());

                session.CommitTransaction();
            }
        }

#if NUNIT
        [Fact]
        [Explicit("Can run only locally because it require a MongoDb Replica set")]
#endif
        public void Stream_was_persisted()
        {
            var commit = Persistence.GetCommit(_commit.CheckpointToken);
            commit.Should().NotBeNull();
            commit.StreamId.Should().Be(_streamId);
        }

        protected override void Cleanup()
        {
            Persistence.Drop();
            Persistence.Dispose();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_a_Transaction_Was_Aborted : AbstractTransactionTests
    {
        private string _streamId;
        protected override void Because()
        {
            using (var session = MongoClient.StartSession())
            {
                session.StartTransaction();

                _streamId = Guid.NewGuid().ToString();
                Persistence.Commit(_streamId.BuildAttempt());

                session.AbortTransaction();
            }
        }

#if NUNIT
        [Fact]
        [Explicit("Can run only locally because it require a MongoDb Replica set")]
#endif
        public void Stream_was_not_persisted()
        {
            var stream = Persistence.GetFrom(_streamId, 0, int.MaxValue);
            stream.Count().Should().Be(0);
        }

        protected override void Cleanup()
        {
            Persistence.Drop();
            Persistence.Dispose();
        }
    }

}
