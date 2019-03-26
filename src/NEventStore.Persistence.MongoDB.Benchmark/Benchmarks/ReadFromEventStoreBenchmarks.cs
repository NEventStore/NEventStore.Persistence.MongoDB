using BenchmarkDotNet.Attributes;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using System;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, targetCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class ReadFromEventStoreBenchmarks
    {
        //[Params(100, 1000, 10000, 100000)]
        [Params(100, 1000, 10000)]
        public int CommitsToWrite { get; set; }

        private static readonly Guid StreamId = Guid.NewGuid(); // aggregate identifier
        private readonly IStoreEvents _eventStore;

        public ReadFromEventStoreBenchmarks()
        {
            _eventStore = EventStoreHelpers.WireupEventStore();
        }

        [GlobalSetup()]
        public void ReadSetup()
        {
            _eventStore.Advanced.Purge();

            using (var stream = _eventStore.CreateStream(StreamId))
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

        [Benchmark]
        public void ReadFromEventStore()
        {
            var commits = _eventStore.Advanced.GetFrom(Bucket.Default, 0);
            foreach (var c in commits)
            {
                // just iterate through all the commits
                // Console.WriteLine(c);
            }
        }

        public void ProfileWithVisualStudio(int commitsToWrite)
        {
            this.CommitsToWrite = commitsToWrite;
            this.ReadSetup();
            this.ReadFromEventStore();
        }
    }
}
