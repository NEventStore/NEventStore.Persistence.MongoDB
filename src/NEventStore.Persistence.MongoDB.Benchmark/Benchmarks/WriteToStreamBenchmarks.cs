using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class WriteToStreamBenchmarks
    {
        //[Params(100, 1000, 10000, 100000)]
        [Params(100, 1000, 10000)]
        public int CommitsToWrite { get; set; }

        private static readonly Guid StreamId = Guid.NewGuid(); // aggregate identifier
        private readonly IStoreEvents _eventStore;

        public WriteToStreamBenchmarks()
        {
            _eventStore = EventStoreHelpers.WireupEventStore();
        }

        [GlobalSetup]
        public void ClearEventStore()
        {
            _eventStore.Advanced.Purge();
        }

        [Benchmark]
        public void WriteToStream()
        {
            // we can call CreateStream(StreamId) if we know there isn't going to be any data.
            // or we can call OpenStream(StreamId, 0, int.MaxValue) to read all commits,
            // if no commits exist then it creates a new stream for us.
            using (var stream = _eventStore.OpenStream(StreamId, 0, int.MaxValue))
            {
                // add XXX commits to the stream
                for (int i = 0; i < CommitsToWrite; i++)
                {
                    var @event = new SomeDomainEvent { Value = i.ToString() };
                    stream.Add(new EventMessage { Body = @event });
                    stream.CommitChanges(Guid.NewGuid());
                }
            }
        }
    }
}
