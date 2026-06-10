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
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class StreamRevisionWindowBenchmarks
    {
        [Params(1000, 10000)]
        public int TotalCommitsInStream { get; set; }

        [Params(10, 100, 1000)]
        public int RevisionWindowSize { get; set; }

        private readonly string _streamId = Guid.NewGuid().ToString();
        private readonly IPersistStreams _persistence;

        public StreamRevisionWindowBenchmarks()
        {
            _persistence = (IPersistStreams)EventStoreHelpers.WireupEventStore().Advanced;
        }

        [GlobalSetup]
        public void Setup()
        {
            _persistence.Purge();

            for (int i = 1; i <= TotalCommitsInStream; i++)
            {
                var attempt = new CommitAttempt(
                    bucketId: Bucket.Default,
                    streamId: _streamId,
                    streamRevision: i,
                    commitId: Guid.NewGuid(),
                    commitSequence: i,
                    commitStamp: DateTime.UtcNow,
                    headers: null,
                    events:
                    [
                        new EventMessage { Body = new SomeDomainEvent { Value = i.ToString() } }
                    ]);

                _persistence.Commit(attempt);
            }
        }

        [Benchmark]
        public int ReadTailRevisionWindow()
        {
            var minRevision = Math.Max(1, TotalCommitsInStream - RevisionWindowSize + 1);
            var maxRevision = TotalCommitsInStream;
            return _persistence.GetFrom(Bucket.Default, _streamId, minRevision, maxRevision).Count();
        }
    }
}
