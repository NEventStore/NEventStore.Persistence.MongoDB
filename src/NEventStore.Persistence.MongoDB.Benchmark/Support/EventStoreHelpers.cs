using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NEventStore.Persistence.MongoDB;
using NEventStore.Serialization;
using System;
using System.Threading;

namespace NEventStore.Persistence.MongoDB.Benchmark.Support
{
    internal static class EventStoreHelpers
    {
        private const string EnvVarConnectionStringKey = "NEventStore.MongoDB";

        private static int _serializersRegistered;

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

        /// <summary>
        /// Registers MongoDB BSON serializers required for CSharpLegacy GUID compatibility.
        /// Safe to call multiple times within the same process; registration happens only once.
        /// </summary>
        internal static void EnsureSerializersRegistered()
        {
            if (Interlocked.Exchange(ref _serializersRegistered, 1) == 0)
            {
                // MongoDb 3.0.0 GUID serialization changed; register CSharpLegacy for backward compatibility.
                BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
                // when serializing guid in a Dictionary<object, object> take a look at the comment here:
                // https://jira.mongodb.org/browse/CSHARP-4987?jql=text%20~%20%22GuidRepresentation%20dictionary%22
                BsonSerializer.RegisterSerializer(new ObjectSerializer(
                    BsonSerializer.LookupDiscriminatorConvention(typeof(object)), GuidRepresentation.CSharpLegacy, ObjectSerializer.AllAllowedTypes));
            }
        }

        internal static IStoreEvents WireupEventStore(MongoPersistenceOptions? options = null)
        {
            EnsureSerializersRegistered();

            return Wireup.Init()
               // .LogToOutputWindow(LogLevel.Verbose)
               // .LogToConsoleWindow(LogLevel.Verbose)
               .UsingMongoPersistence(() => GetConnectionString(), new DocumentObjectSerializer(), options)
               .InitializeStorageEngine()
#if NET472_OR_GREATER
               .TrackPerformanceInstance("example")
#endif
               // .HookIntoPipelineUsing(new[] { new AuthorizationPipelineHook() })
               .Build();
        }
    }
}
