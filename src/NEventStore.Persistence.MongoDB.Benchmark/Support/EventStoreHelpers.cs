using NEventStore.Serialization;
using System;

namespace NEventStore.Persistence.MongoDB.Benchmark.Support
{
    internal static class EventStoreHelpers
    {
        private const string EnvVarConnectionStringKey = "NEventStore.MongoDB";

        internal static string GetConnectionString()
        {
            string connectionString = Environment.GetEnvironmentVariable(EnvVarConnectionStringKey, EnvironmentVariableTarget.Process);

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
#if NET472_OR_GREATER
               .TrackPerformanceInstance("example")
#endif
               // .HookIntoPipelineUsing(new[] { new AuthorizationPipelineHook() })
               .Build();
        }
    }
}
