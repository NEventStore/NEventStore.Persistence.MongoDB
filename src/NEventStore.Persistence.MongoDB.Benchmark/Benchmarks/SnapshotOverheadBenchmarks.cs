using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Measures the overhead of stream-head maintenance on the write path.
    ///
    /// <para>
    /// When <see cref="DisableSnapshotSupport"/> is <c>false</c> (default), the engine writes a
    /// stream-head document on every commit.  When <c>true</c>, that write is skipped entirely.
    /// </para>
    /// <para>
    /// When <see cref="PersistStreamHeadsOnBackgroundThread"/> is <c>true</c> (default), the
    /// stream-head write happens on a background task so commit latency is lower but resource
    /// usage is higher.  When <c>false</c> it happens inline (useful for testing; higher latency).
    /// </para>
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class SnapshotOverheadBenchmarks
    {
        [Params(100, 1000)]
        public int CommitsToWrite { get; set; }

        [Params(true, false)]
        public bool DisableSnapshotSupport { get; set; }

        [Params(true, false)]
        public bool PersistStreamHeadsOnBackgroundThread { get; set; }

        private static readonly Guid StreamId = Guid.NewGuid();
        private IStoreEvents _eventStore = null!;

        [GlobalSetup]
        public void Setup()
        {
            EventStoreHelpers.EnsureSerializersRegistered();

            var options = new MongoPersistenceOptions
            {
                DisableSnapshotSupport = DisableSnapshotSupport,
                PersistStreamHeadsOnBackgroundThread = PersistStreamHeadsOnBackgroundThread
            };

            _eventStore = EventStoreHelpers.WireupEventStore(options);
            _eventStore.Advanced.Purge();
        }

        [Benchmark]
        public void WriteToStream()
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
