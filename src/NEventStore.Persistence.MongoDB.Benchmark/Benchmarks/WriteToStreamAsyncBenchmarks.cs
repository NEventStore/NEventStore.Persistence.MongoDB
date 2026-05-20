using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Benchmarks the async commit path via <see cref="IPersistStreams.CommitAsync"/>.
    /// Mirrors <see cref="WriteToStreamBenchmarks"/> against the async engine path.
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class WriteToStreamAsyncBenchmarks
    {
        //[Params(100, 1000, 10000, 100000)]
        [Params(100, 1000, 10000)]
        public int CommitsToWrite { get; set; }

        private static readonly string StreamId = Guid.NewGuid().ToString();
        private IPersistStreams _persistence = null!;
        private int _streamRevision;
        private int _commitSequence;

        public WriteToStreamAsyncBenchmarks()
        {
            var store = EventStoreHelpers.WireupEventStore();
            _persistence = (IPersistStreams)store.Advanced;
        }

        [GlobalSetup]
        public void Setup()
        {
            _persistence.Purge();
            _streamRevision = 0;
            _commitSequence = 0;
        }

        [Benchmark]
        public async Task WriteToStreamAsync()
        {
            for (int i = 0; i < CommitsToWrite; i++)
            {
                _streamRevision++;
                _commitSequence++;
                var attempt = new CommitAttempt(
                    streamId: StreamId,
                    streamRevision: _streamRevision,
                    commitId: Guid.NewGuid(),
                    commitSequence: _commitSequence,
                    commitStamp: DateTime.UtcNow,
                    headers: null,
                    events: new List<EventMessage> { new EventMessage { Body = new SomeDomainEvent { Value = i.ToString() } } }
                );
                await _persistence.CommitAsync(attempt, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
