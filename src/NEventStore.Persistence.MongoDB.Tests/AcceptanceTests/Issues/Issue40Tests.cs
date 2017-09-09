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
    public class Issue40Tests
    {
#if MSTEST
        [TestClass]
#endif
        public class verify_ability_to_opt_out_stream_head : PersistenceEngineConcern
        {
            private IMongoCollection<BsonDocument> _streamHeads;

            public verify_ability_to_opt_out_stream_head()
            {
                var options = new MongoPersistenceOptions();
                options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;
                options.DisableSnapshotSupport = true;

                var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
                _streamHeads = db.GetCollection<BsonDocument>("Streams");

                PersistenceEngineFixture.Options = options;

                // workaround for test initialization to have uniform config for all 3 test frameworks
                // we can't use ClassInitialize, TestFixtureSetup or SetFixture
                Reinitialize();

                // reset this immediately, hopefully will not impact other tests
                PersistenceEngineFixture.Options = null;
            }

            protected override void Context()
            {

            }

            protected override void Because()
            {
                var streamId = Guid.NewGuid().ToString();
                CommitAttempt attempt = streamId.BuildAttempt();
                Persistence.Commit(streamId.BuildAttempt());
            }

            [Fact]
            public void no_stream_heads_are_saved()
            {
                var heads = _streamHeads.Find(Builders<BsonDocument>.Filter.Empty);
                heads.Count().Should().Be(0);
            }

            protected override void Cleanup()
            {
                base.Cleanup();
            }
        }

#if MSTEST
        [TestClass]
#endif
        public class calling_AddShapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
        {

            public calling_AddShapshot_function_when_snapshot_disabled_throws()
            {
                var options = new MongoPersistenceOptions();
                options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;
                options.DisableSnapshotSupport = true;

                var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());

                PersistenceEngineFixture.Options = options;

                // workaround for test initialization to have uniform config for all 3 test frameworks
                // we can't use ClassInitialize, TestFixtureSetup or SetFixture
                Reinitialize();

                // reset this immediately, hopefully will not impact other tests
                PersistenceEngineFixture.Options = null;
            }

            protected override void Context()
            {

            }

            Exception _ex;

            protected override void Because()
            {
                var streamId = Guid.NewGuid().ToString();
                CommitAttempt attempt = streamId.BuildAttempt();
                Persistence.Commit(streamId.BuildAttempt());

                _ex = Catch.Exception(() => Persistence.AddSnapshot(new Snapshot(streamId, 1, new object())));
            }

            [Fact]
            public void exception_was_thrown()
            {
                _ex.Should().BeOfType<NotSupportedException>();
            }

            protected override void Cleanup()
            {
                base.Cleanup();
            }
        }

#if MSTEST
        [TestClass]
#endif
        public class calling_GetSnapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
        {

            public calling_GetSnapshot_function_when_snapshot_disabled_throws()
            {
                var options = new MongoPersistenceOptions();
                options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;
                options.DisableSnapshotSupport = true;

                var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());

                PersistenceEngineFixture.Options = options;

                // workaround for test initialization to have uniform config for all 3 test frameworks
                // we can't use ClassInitialize, TestFixtureSetup or SetFixture
                Reinitialize();

                // reset this immediately, hopefully will not impact other tests
                PersistenceEngineFixture.Options = null;
            }

            protected override void Context()
            {

            }

            Exception _ex;

            protected override void Because()
            {
                var streamId = Guid.NewGuid().ToString();
                CommitAttempt attempt = streamId.BuildAttempt();
                Persistence.Commit(streamId.BuildAttempt());

                _ex = Catch.Exception(() => Persistence.GetSnapshot(streamId, 1));
            }

            [Fact]
            public void exception_was_thrown()
            {
                _ex.Should().BeOfType<NotSupportedException>();
            }

            protected override void Cleanup()
            {
                base.Cleanup();
            }
        }

#if MSTEST
        [TestClass]
#endif
        public class calling_GetStreamToSnapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
        {

            public calling_GetStreamToSnapshot_function_when_snapshot_disabled_throws()
            {
                var options = new MongoPersistenceOptions();
                options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;
                options.DisableSnapshotSupport = true;

                var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());

                PersistenceEngineFixture.Options = options;

                // workaround for test initialization to have uniform config for all 3 test frameworks
                // we can't use ClassInitialize, TestFixtureSetup or SetFixture
                Reinitialize();
                // reset this immediately, hopefully will not impact other tests
                PersistenceEngineFixture.Options = null;
            }

            protected override void Context()
            {

            }

            Exception _ex;

            protected override void Because()
            {
                var streamId = Guid.NewGuid().ToString();
                CommitAttempt attempt = streamId.BuildAttempt();
                Persistence.Commit(streamId.BuildAttempt());

                _ex = Catch.Exception(() => Persistence.GetStreamsToSnapshot("testBucket", 1));
            }

            [Fact]
            public void exception_was_thrown()
            {
                _ex.Should().BeOfType<NotSupportedException>();
            }

            protected override void Cleanup()
            {
                base.Cleanup();
            }
        }

    }
}
