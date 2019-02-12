using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NEventStore.Persistence.AcceptanceTests.BDD;
using FluentAssertions;
using MongoDB.Bson.Serialization.Conventions;
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

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
#if MSTEST
        [TestClass]
#endif
    public class Issue_55_CamelCase_Convention_Should_Not_Be_Applied_To_MongoCommit : PersistenceEngineConcern
    {
        private IMongoCollection<BsonDocument> _commits;
        private CommitAttempt expectedAttempt;

        public Issue_55_CamelCase_Convention_Should_Not_Be_Applied_To_MongoCommit()
        {
            var options = new MongoPersistenceOptions();
            var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
            _commits = db.GetCollection<BsonDocument>("Commits");
        }

        /// <summary>
        /// Given a Convention Pack applied to everything
        /// </summary>
        protected override void Context()
        {
            var pack = new ConventionPack
                           {
                               new CamelCaseElementNameConvention(),
                           };
            ConventionRegistry.Register("camel case", pack, t => true);
        }

        /// <summary>
        /// When persisting a commit (The Database should not exists or be empty)
        /// </summary>
        protected override void Because()
        {
            var streamId = Guid.NewGuid().ToString();
            expectedAttempt = streamId.BuildAttempt();
            Persistence.Commit(expectedAttempt);
        }

        [Fact]
        public void Persisted_MongoCommit_Should_Be_TileCase()
        {
            // read the commit As BsonDocument and check the serialization
            var commit = _commits.AsQueryable().Single();
            // read all the time case properties and expect them to be there
            commit.Contains(MongoCommitFields.BucketId).Should().BeTrue();
            commit.Contains(MongoCommitFields.CheckpointNumber).Should().BeTrue();
            commit.Contains(MongoCommitFields.CommitId).Should().BeTrue();
            commit.Contains(MongoCommitFields.CommitSequence).Should().BeTrue();
            commit.Contains(MongoCommitFields.CommitStamp).Should().BeTrue();
            commit.Contains(MongoCommitFields.Events).Should().BeTrue();
            commit.Contains(MongoCommitFields.Headers).Should().BeTrue();
            commit.Contains(MongoCommitFields.Payload).Should().BeTrue();
            commit.Contains(MongoCommitFields.StreamId).Should().BeTrue();
            commit.Contains(MongoCommitFields.StreamRevision).Should().BeTrue();
            commit.Contains(MongoCommitFields.StreamRevisionFrom).Should().BeTrue();
            commit.Contains(MongoCommitFields.StreamRevisionTo).Should().BeTrue();
        }
    }
}
