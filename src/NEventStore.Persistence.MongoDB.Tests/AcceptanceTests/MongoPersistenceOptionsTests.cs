using NEventStore.Persistence.AcceptanceTests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests.BDD;
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
        private IMongoDatabase _db;
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
            var settings = _db.Client.Settings;
            Assert.AreEqual(TestApplicationName, settings.ApplicationName);
            Assert.AreEqual(TestReplicaSetName, settings.ReplicaSetName);
        }
    }
}
