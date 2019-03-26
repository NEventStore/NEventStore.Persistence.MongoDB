using BenchmarkDotNet.Running;
using NEventStore.Persistence.MongoDB.Benchmark.Benchmarks;
using System;

namespace NEventStore.Persistence.MongoDB.Benchmark
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            //BenchmarkRunner.Run<WriteToStreamBenchmarks>();
            //BenchmarkRunner.Run<ReadFromStreamBenchmarks>();
            BenchmarkRunner.Run<ReadFromEventStoreBenchmarks>();

            //var p = new ReadFromEventStoreBenchmarks();
            //p.ProfileWithVisualStudio(1000);
        }
    }
}
