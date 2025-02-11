﻿using NEventStore.Persistence.MongoDB;
using NEventStore.Persistence.MongoDB.Tests;

// ReSharper disable once CheckNamespace
namespace NEventStore.Persistence.AcceptanceTests
{
    public partial class PersistenceEngineFixture
    {
        public PersistenceEngineFixture()
        {
            _createPersistence = _ => new AcceptanceTestMongoPersistenceFactory(Options).Build();
        }

        public static MongoPersistenceOptions? Options { get; set; }
    }
}