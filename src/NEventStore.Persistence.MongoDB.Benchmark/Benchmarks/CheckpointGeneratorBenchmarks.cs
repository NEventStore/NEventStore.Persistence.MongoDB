using BenchmarkDotNet.Attributes;
using MongoDB.Bson;
using NEventStore.Persistence.MongoDB;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using NEventStore.Persistence.MongoDB.Support;
using System;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Compares the per-commit checkpoint overhead of the two built-in checkpoint generators:
    /// - "Always": AlwaysQueryDbForNextValueCheckpointGenerator — one extra DB read per commit (default).
    /// - "InMemory": InMemoryCheckpointGenerator — in-memory increment; DB only on duplicate signal.
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class CheckpointGeneratorBenchmarks
    {
        [Params(100, 1000)]
        public int CommitsToWrite { get; set; }

        [Params("Always", "InMemory")]
        public string GeneratorType { get; set; } = "Always";

        private static readonly Guid StreamId = Guid.NewGuid();
        private IStoreEvents _eventStore = null!;

        [GlobalSetup]
        public void Setup()
        {
            EventStoreHelpers.EnsureSerializersRegistered();

            var options = new MongoPersistenceOptions();

            if (GeneratorType == "InMemory")
            {
                var db = options.ConnectToDatabase(EventStoreHelpers.GetConnectionString());
                var collection = db.GetCollection<BsonDocument>("Commits");
                options.CheckpointGenerator = new InMemoryCheckpointGenerator(collection);
            }
            // "Always" is the engine default — leave CheckpointGenerator null.

            _eventStore = EventStoreHelpers.WireupEventStore(options);
            _eventStore.Advanced.Purge();
        }

        [Benchmark]
        public void WriteWithCheckpointGenerator()
        {
            using var stream = _eventStore.OpenStream(StreamId, 0, int.MaxValue);
            for (int i = 0; i < CommitsToWrite; i++)
            {
                stream.Add(new EventMessage { Body = new SomeDomainEvent { Value = i.ToString() } });
                stream.CommitChanges(Guid.NewGuid());
            }
        }
    }
}
