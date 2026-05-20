using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Compares global checkpoint scans across all buckets vs a specific bucket.
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class GlobalCheckpointReadBenchmarks
    {
        [Params(100, 1000)]
        public int CommitsPerBucket { get; set; }

        [Params(1, 3)]
        public int ExtraBuckets { get; set; }

        private readonly IStoreEvents _eventStore;
        private readonly IPersistStreams _persistence;

        public GlobalCheckpointReadBenchmarks()
        {
            _eventStore = EventStoreHelpers.WireupEventStore();
            _persistence = (IPersistStreams)_eventStore.Advanced;
        }

        [GlobalSetup]
        public void Setup()
        {
            _persistence.Purge();
            SeedBucket(Bucket.Default, CommitsPerBucket);

            for (int b = 0; b < ExtraBuckets; b++)
            {
                SeedBucket($"bench-bucket-{b}", CommitsPerBucket);
            }
        }

        [Benchmark]
        public int ReadFromBucketCheckpoint()
        {
            return _persistence.GetFrom(Bucket.Default, 0).Count();
        }

        [Benchmark]
        public int ReadFromAllBucketsCheckpoint()
        {
            return _persistence.GetFrom(0).Count();
        }

        private void SeedBucket(string bucketId, int commits)
        {
            var streamId = Guid.NewGuid().ToString("N");
            for (int i = 1; i <= commits; i++)
            {
                var attempt = new CommitAttempt(
                    bucketId: bucketId,
                    streamId: streamId,
                    streamRevision: i,
                    commitId: Guid.NewGuid(),
                    commitSequence: i,
                    commitStamp: DateTime.UtcNow,
                    headers: null,
                    events: new List<EventMessage>
                    {
                        new EventMessage { Body = new SomeDomainEvent { Value = i.ToString() } }
                    }
                );
                _persistence.Commit(attempt);
            }
        }
    }
}
