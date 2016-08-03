using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Should;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public class Issue40Tests
    {
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
            public void holes_are_presents()
            {
                var heads = _streamHeads.Find(Builders<BsonDocument>.Filter.Empty);
                Assert.Equal(0, heads.Count());
            }

            protected override void Cleanup()
            {
                PersistenceEngineFixture.Options = null;
                base.Cleanup();
            }
        }

        public class calling_AddShapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
        {
           
            public calling_AddShapshot_function_when_snapshot_disabled_throws()
            {
                var options = new MongoPersistenceOptions();
                options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;
                options.DisableSnapshotSupport = true;

                var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());

                PersistenceEngineFixture.Options = options;
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
                _ex.ShouldBeInstanceOf<NotSupportedException>();
            }

            protected override void Cleanup()
            {
                PersistenceEngineFixture.Options = null;
                base.Cleanup();
            }
        }

        public class calling_GetSnapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
        {
         
            public calling_GetSnapshot_function_when_snapshot_disabled_throws()
            {
                var options = new MongoPersistenceOptions();
                options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;
                options.DisableSnapshotSupport = true;

                var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());

                PersistenceEngineFixture.Options = options;
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
                _ex.ShouldBeInstanceOf<NotSupportedException>();
            }

            protected override void Cleanup()
            {
                PersistenceEngineFixture.Options = null;
                base.Cleanup();
            }
        }

        public class calling_GetStreamToSnapshot_function_when_snapshot_disabled_throws : PersistenceEngineConcern
        {

            public calling_GetStreamToSnapshot_function_when_snapshot_disabled_throws()
            {
                var options = new MongoPersistenceOptions();
                options.ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;
                options.DisableSnapshotSupport = true;

                var db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());

                PersistenceEngineFixture.Options = options;
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
                _ex.ShouldBeInstanceOf<NotSupportedException>();
            }

            protected override void Cleanup()
            {
                PersistenceEngineFixture.Options = null;
                base.Cleanup();
            }
        }

    }
}
