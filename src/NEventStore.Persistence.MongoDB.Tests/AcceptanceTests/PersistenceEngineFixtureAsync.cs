using NEventStore.Persistence.MongoDB;
using NEventStore.Persistence.MongoDB.Tests;

// ReSharper disable once CheckNamespace
namespace NEventStore.Persistence.AcceptanceTests.Async
{
    public partial class PersistenceEngineFixtureAsync
    {
        public PersistenceEngineFixtureAsync()
        {
            _createPersistence = _ => new AcceptanceTestMongoPersistenceFactory(Options).Build();
        }

        public static MongoPersistenceOptions? Options { get; set; }
    }
}