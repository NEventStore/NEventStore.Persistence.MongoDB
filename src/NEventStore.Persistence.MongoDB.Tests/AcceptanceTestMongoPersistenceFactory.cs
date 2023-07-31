namespace NEventStore.Persistence.MongoDB.Tests {
    using System;
    using global::MongoDB.Bson.Serialization.Serializers;
    using global::MongoDB.Bson.Serialization;
    using NEventStore.Serialization;
#if MSTEST
    using global::MongoDB.Driver;
#endif

    public class AcceptanceTestMongoPersistenceFactory : MongoPersistenceFactory {
        private const string EnvVarConnectionStringKey = "NEventStore.MongoDB";

        static AcceptanceTestMongoPersistenceFactory() {
            // MongoDb serialization changed
            // What Object Types Can Be Serialized?
            // https://www.mongodb.com/docs/drivers/csharp/current/faq/#what-object-types-can-be-serialized-
            BsonSerializer.RegisterSerializer(new ObjectSerializer(ObjectSerializer.AllAllowedTypes));
        }

        public AcceptanceTestMongoPersistenceFactory(MongoPersistenceOptions options = null)
            : base(
                GetConnectionString,
                new DocumentObjectSerializer(),
                ConfigureOptionsForTesting(options ?? new MongoPersistenceOptions())
            ) { }

        private static MongoPersistenceOptions ConfigureOptionsForTesting(MongoPersistenceOptions mongoPersistenceOptions) {
            mongoPersistenceOptions.PersistStreamHeadsOnBackgroundThread = false;
            return mongoPersistenceOptions;
        }

        internal static string GetConnectionString() {
            string connectionString = Environment.GetEnvironmentVariable(EnvVarConnectionStringKey, EnvironmentVariableTarget.Process);

            if (connectionString == null) {
                string message = string.Format(
                    "Cannot initialize acceptance tests for Mongo. Cannot find the '{0}' environment variable. Please ensure " +
                    "you have correctly setup the connection string environment variables. Refer to the " +
                    "NEventStore wiki for details.",
                    EnvVarConnectionStringKey);
                throw new InvalidOperationException(message);
            }

            connectionString = connectionString.TrimStart('"').TrimEnd('"');

#if MSTEST
            // quick and dirty solution to avoid tests clashing when executed in parallel
            var builder = new MongoUrlBuilder(connectionString);
            builder.DatabaseName += Guid.NewGuid().ToString();
            return builder.ToString();
#else
            return connectionString;
#endif
        }
    }
}
