using NEventStore.Persistence.AcceptanceTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests.BDD;
using Microsoft.ApplicationInsights.Extensibility.Implementation.Tracing;

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
    /// <summary>
    /// Change some settings and see if they are correctly applied
    /// </summary>
#if MSTEST
    [TestClass]
#endif
    public class When_customizing_MongoClientSettings : PersistenceEngineConcern
    {
        private IMongoDatabase? _db;
        private readonly string TestReplicaSetName = Guid.NewGuid().ToString();
        private readonly string TestApplicationName = Guid.NewGuid().ToString();

        protected override void Because()
        {
            var options = new MongoPersistenceOptions(
                (settings) =>
                {
                    settings.ApplicationName = TestApplicationName;
                    settings.ReplicaSetName = TestReplicaSetName;
                });
            _db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
        }

        [Fact]
        public void Settings_are_correctly_applied()
        {
            var settings = _db!.Client.Settings;
            Assert.That(TestApplicationName, Is.EqualTo(settings.ApplicationName));
            Assert.That(TestReplicaSetName, Is.EqualTo(settings.ReplicaSetName));
        }
    }

    /// <summary>
    /// Create a MongoPersistenceOptions with a custom IMongoClient that match the connection string
    /// </summary>
#if MSTEST
    [TestClass]
#endif
    public class When_customizing_MongoPersistenceOptions_passing_correct_IMongoClient : PersistenceEngineConcern
    {
        private IMongoClient? _mongoClient;
        private IMongoDatabase? _db;
        private Exception? _ex;

        protected override void Because()
        {
            _mongoClient = new MongoClient(AcceptanceTestMongoPersistenceFactory.GetConnectionString());

            var options = new MongoPersistenceOptions(
                mongoClient: _mongoClient);

            _ex = Catch.Exception(
                () => _db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
                );
        }

        [Fact]
        public void No_exception_is_thrown()
        {
            Assert.That(_ex, Is.Null);
        }

        [Fact]
        public void Database_was_correctly_created()
        {
            Assert.That(_db, Is.Not.Null);
            Assert.That(_mongoClient, Is.EqualTo(_db!.Client));
        }
    }

    /// <summary>
    /// Create a MongoPersistenceOptions with a custom IMongoClient that match the connection string
    /// </summary>
#if MSTEST
    [TestClass]
#endif
    public class When_customizing_MongoPersistenceOptions_passing_correct_IMongoClient_cluster : PersistenceEngineConcern
    {
        private IMongoClient? _mongoClient;
        private IMongoDatabase? _db;
        private Exception? _ex;
        private const string connectionString = "mongodb://localhost:50001,localhost:50002/NEventStore";

        protected override void Because()
        {
            _mongoClient = new MongoClient(connectionString);

            var options = new MongoPersistenceOptions(
                mongoClient: _mongoClient);

            _ex = Catch.Exception(
                () => _db = options.ConnectToDatabase(connectionString)
                );
        }

        [Fact]
        public void No_exception_is_thrown()
        {
            Assert.That(_ex, Is.Null);
        }

        [Fact]
        public void Database_was_correctly_created()
        {
            Assert.That(_db, Is.Not.Null);
            Assert.That(_mongoClient, Is.EqualTo(_db!.Client));
        }
    }

    /// <summary>
    /// Create a MongoPersistenceOptions with a custom IMongoClient that does not match the connection string
    /// </summary>
#if MSTEST
    [TestClass]
#endif
    public class When_customizing_MongoPersistenceOptions_passing_incorrect_IMongoClient : PersistenceEngineConcern
    {
        private IMongoDatabase? _db;

        private Exception? _ex;

        protected override void Because()
        {
            var client = new MongoClient("mongodb://127.0.0.2");

            var options = new MongoPersistenceOptions(
                mongoClient: client);

            _ex = Catch.Exception(
                () => _db = options.ConnectToDatabase(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
                );
        }

        [Fact]
        public void Exception_is_thrown()
        {
            Assert.That(_ex, Is.Not.Null);
            Assert.That(_ex!.Message, Is.EqualTo("MongoClient instance was created with a different connection string: host and port should match."));
        }

        [Fact]
        public void Database_was_not_created()
        {
            Assert.That(_db, Is.Null);
        }
    }

    /// <summary>
    /// Create a MongoPersistenceOptions with a custom IMongoClient that does not match the connection string
    /// </summary>
#if MSTEST
    [TestClass]
#endif
    public class When_customizing_MongoPersistenceOptions_passing_incorrect_IMongoClient_cluster : PersistenceEngineConcern
    {
        private IMongoClient? _mongoClient;
        private IMongoDatabase? _db;
        private Exception? _ex;
        private const string connectionString1 = "mongodb://localhost:50001,localhost:50002/NEventStore";
        private const string connectionString2 = "mongodb://localhost,localhost:50002/NEventStore";

        protected override void Because()
        {
            _mongoClient = new MongoClient(connectionString1);

            var options = new MongoPersistenceOptions(
                mongoClient: _mongoClient);

            _ex = Catch.Exception(
                () => _db = options.ConnectToDatabase(connectionString2)
                );
        }

        [Fact]
        public void Exception_is_thrown()
        {
            Assert.That(_ex, Is.Not.Null);
            Assert.That(_ex!.Message, Is.EqualTo("MongoClient instance was created with a different connection string: hosts and ports should match."));
        }

        [Fact]
        public void Database_was_not_created()
        {
            Assert.That(_db, Is.Null);
        }
    }
}
