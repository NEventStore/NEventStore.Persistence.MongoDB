using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;
using System.Linq;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Measures per-stream reads over focused revision windows.
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class StreamRevisionWindowBenchmarks
    {
        [Params(1000, 10000)]
        public int TotalCommitsInStream { get; set; }

        [Params(10, 100, 1000)]
        public int RevisionWindowSize { get; set; }

        private static readonly Guid StreamId = Guid.NewGuid();
        private readonly IStoreEvents _eventStore;
        private readonly IPersistStreams _persistence;

        public StreamRevisionWindowBenchmarks()
        {
            _eventStore = EventStoreHelpers.WireupEventStore();
            _persistence = (IPersistStreams)_eventStore.Advanced;
        }

        [GlobalSetup]
        public void Setup()
        {
            _persistence.Purge();

            using var stream = _eventStore.CreateStream(StreamId);
            for (int i = 0; i < TotalCommitsInStream; i++)
            {
                stream.Add(new EventMessage { Body = new SomeDomainEvent { Value = i.ToString() } });
                stream.CommitChanges(Guid.NewGuid());
            }
        }

        [Benchmark]
        public int ReadTailRevisionWindow()
        {
            var minRevision = Math.Max(1, TotalCommitsInStream - RevisionWindowSize + 1);
            var maxRevision = TotalCommitsInStream;
            return _persistence.GetFrom(Bucket.Default, StreamId.ToString(), minRevision, maxRevision).Count();
        }
    }
}
