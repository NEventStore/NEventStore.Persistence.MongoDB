using BenchmarkDotNet.Running;
using System.Reflection;

namespace NEventStore.Persistence.MongoDB.Benchmark
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
        }
    }
}
