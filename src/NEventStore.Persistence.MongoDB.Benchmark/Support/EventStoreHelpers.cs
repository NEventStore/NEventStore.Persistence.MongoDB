using NEventStore.Serialization;
using System;

namespace NEventStore.Persistence.MongoDB.Benchmark.Support
{
    internal static class EventStoreHelpers
    {
        private const string EnvVarConnectionStringKey = "NEventStore.MongoDB";

        internal static string GetConnectionString()
        {
#if !NETSTANDARD1_6
            string connectionString = Environment.GetEnvironmentVariable(EnvVarConnectionStringKey, EnvironmentVariableTarget.Process);
#else
            string connectionString = Environment.GetEnvironmentVariable(EnvVarConnectionStringKey);
#endif

            if (connectionString == null)
            {
                string message = string.Format(
                    "Cannot initialize acceptance tests for Mongo. Cannot find the '{0}' environment variable. Please ensure " +
                    "you have correctly setup the connection string environment variables. Refer to the " +
                    "NEventStore wiki for details.",
                    EnvVarConnectionStringKey);
                throw new InvalidOperationException(message);
            }

            return connectionString.TrimStart('"').TrimEnd('"');
        }

        internal static IStoreEvents WireupEventStore()
        {
            return Wireup.Init()
               // .LogToOutputWindow(LogLevel.Verbose)
               // .LogToConsoleWindow(LogLevel.Verbose)
               .UsingMongoPersistence(GetConnectionString(), new DocumentObjectSerializer())
               .InitializeStorageEngine()
#if !NETSTANDARD1_6 && !NETSTANDARD2_0
               .TrackPerformanceInstance("example")
#endif
               // .HookIntoPipelineUsing(new[] { new AuthorizationPipelineHook() })
               .Build();
        }
    }
}
