using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Compares full aggregate rebuilds with snapshot-assisted rebuilds over the tail of a long stream.
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class SnapshotAssistedRebuildBenchmarks
    {
        [Params(1000, 10000)]
        public int TotalCommitsInStream { get; set; }

        [Params(10, 100, 1000)]
        public int CommitsAfterSnapshot { get; set; }

        private readonly Guid _streamId = Guid.NewGuid();
        private IStoreEvents _eventStore = null!;
        private ISnapshot _snapshot = null!;

        [GlobalSetup]
        public void Setup()
        {
            var options = new MongoPersistenceOptions
            {
                PersistStreamHeadsOnBackgroundThread = false
            };

            _eventStore = EventStoreHelpers.WireupEventStore(options);
            _eventStore.Advanced.Purge();

            using (var stream = _eventStore.CreateStream(_streamId))
            {
                for (int i = 1; i <= TotalCommitsInStream; i++)
                {
                    stream.Add(new EventMessage { Body = new SomeDomainEvent { Value = i.ToString() } });
                    stream.CommitChanges(Guid.NewGuid());
                }
            }

            var snapshotRevision = Math.Max(1, TotalCommitsInStream - CommitsAfterSnapshot);
            _snapshot = new Snapshot(_streamId.ToString(), snapshotRevision, new SomeDomainEvent { Value = snapshotRevision.ToString() });
            _eventStore.Advanced.AddSnapshot(_snapshot);
        }

        [Benchmark(Baseline = true)]
        public int FullRebuild()
        {
            using var stream = _eventStore.OpenStream(_streamId, 0, int.MaxValue);
            return stream.CommittedEvents.Count;
        }

        [Benchmark]
        public int SnapshotAssistedRebuild()
        {
            using var stream = _eventStore.OpenStream(_snapshot, int.MaxValue);
            return stream.CommittedEvents.Count;
        }
    }
}
