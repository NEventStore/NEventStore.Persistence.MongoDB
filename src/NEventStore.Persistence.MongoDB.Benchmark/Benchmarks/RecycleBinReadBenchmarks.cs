using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;
using System.Linq;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Measures read behavior for active commits vs commits marked as deleted (recycle-bin bucket).
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class RecycleBinReadBenchmarks
    {
        [Params(100, 1000)]
        public int CommitsPerStream { get; set; }

        [Params(1, 5)]
        public int DeletedStreams { get; set; }

        [Params(1, 5)]
        public int ActiveStreams { get; set; }

        private readonly IStoreEvents _eventStore;
        private readonly IPersistStreams _persistence;

        public RecycleBinReadBenchmarks()
        {
            _eventStore = EventStoreHelpers.WireupEventStore();
            _persistence = (IPersistStreams)_eventStore.Advanced;
        }

        [GlobalSetup]
        public void Setup()
        {
            _persistence.Purge();

            for (int i = 0; i < ActiveStreams; i++)
            {
                SeedStreamAndOptionallyDelete(deleteAfterSeed: false);
            }

            for (int i = 0; i < DeletedStreams; i++)
            {
                SeedStreamAndOptionallyDelete(deleteAfterSeed: true);
            }
        }

        [Benchmark]
        public int ReadActiveCommitsFromAllBuckets()
        {
            return _persistence.GetFrom(0).Count();
        }

        [Benchmark]
        public int ReadDeletedCommitsFromRecycleBinBucket()
        {
            return _persistence.GetFrom(MongoSystemBuckets.RecycleBin, 0).Count();
        }

        private void SeedStreamAndOptionallyDelete(bool deleteAfterSeed)
        {
            var streamId = Guid.NewGuid();
            using (var stream = _eventStore.CreateStream(streamId))
            {
                for (int i = 0; i < CommitsPerStream; i++)
                {
                    stream.Add(new EventMessage { Body = new SomeDomainEvent { Value = i.ToString() } });
                    stream.CommitChanges(Guid.NewGuid());
                }
            }

            if (deleteAfterSeed)
            {
                _persistence.DeleteStream(Bucket.Default, streamId.ToString());
            }
        }
    }
}
