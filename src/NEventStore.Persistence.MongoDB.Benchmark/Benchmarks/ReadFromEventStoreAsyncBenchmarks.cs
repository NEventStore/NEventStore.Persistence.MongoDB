using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Benchmarks the async global checkpoint scan via <see cref="IPersistStreams.GetFromAsync(long, IAsyncObserver{ICommit}, CancellationToken)"/>.
    /// Mirrors <see cref="ReadFromEventStoreBenchmarks"/> against the async engine path.
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class ReadFromEventStoreAsyncBenchmarks
    {
        //[Params(100, 1000, 10000, 100000)]
        [Params(100, 1000, 10000)]
        public int CommitsToWrite { get; set; }

        private static readonly Guid StreamId = Guid.NewGuid();
        private readonly IStoreEvents _eventStore;
        private readonly IPersistStreams _persistence;
        private readonly DrainObserver _observer = new DrainObserver();

        public ReadFromEventStoreAsyncBenchmarks()
        {
            _eventStore = EventStoreHelpers.WireupEventStore();
            _persistence = (IPersistStreams)_eventStore.Advanced;
        }

        [GlobalSetup]
        public void ReadSetup()
        {
            _eventStore.Advanced.Purge();

            using var stream = _eventStore.CreateStream(StreamId);
            for (int i = 0; i < CommitsToWrite; i++)
            {
                stream.Add(new EventMessage { Body = new SomeDomainEvent { Value = i.ToString() } });
                stream.CommitChanges(Guid.NewGuid());
            }
        }

        [Benchmark]
        public Task ReadFromEventStoreAsync()
        {
            return _persistence.GetFromAsync(0L, _observer, CancellationToken.None);
        }

        /// <summary>Drains every commit without allocating a collection.</summary>
        private sealed class DrainObserver : IAsyncObserver<ICommit>
        {
            public Task<bool> OnNextAsync(ICommit value, CancellationToken cancellationToken)
                => Task.FromResult(true);

            public Task OnErrorAsync(Exception ex, CancellationToken cancellationToken)
            {
                ExceptionDispatchInfo.Capture(ex).Throw();
                return Task.CompletedTask;
            }

            public Task OnCompletedAsync(CancellationToken cancellationToken)
                => Task.CompletedTask;
        }
    }
}
