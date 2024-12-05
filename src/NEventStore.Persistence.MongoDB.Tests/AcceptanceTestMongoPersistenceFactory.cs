namespace NEventStore.Persistence.MongoDB.Tests {
    using System;
    using global::MongoDB.Bson.Serialization.Serializers;
    using global::MongoDB.Bson.Serialization;
    using NEventStore.Serialization;
    using global::MongoDB.Bson;
#if MSTEST
    using global::MongoDB.Driver;
#endif

    public class AcceptanceTestMongoPersistenceFactory : MongoPersistenceFactory {
        private const string EnvVarConnectionStringKey = "NEventStore.MongoDB";

        static AcceptanceTestMongoPersistenceFactory() {
            // MongoDb serialization changed
            // MongoDb 3.0.0 GUID serialization changed
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
            // when serializing guid in a Dictionary<object, object> take a look at the comment here:
            // https://jira.mongodb.org/browse/CSHARP-4987?jql=text%20~%20%22GuidRepresentation%20dictionary%22
            // it seems you also need to configure the ObjectSerializer
            // What Object Types Can Be Serialized?
            BsonSerializer.RegisterSerializer(new ObjectSerializer(
                BsonSerializer.LookupDiscriminatorConvention(typeof(object)), GuidRepresentation.CSharpLegacy, ObjectSerializer.AllAllowedTypes));
        }

        public AcceptanceTestMongoPersistenceFactory(MongoPersistenceOptions? options = null)
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
            var connectionString = Environment.GetEnvironmentVariable(EnvVarConnectionStringKey, EnvironmentVariableTarget.Process);

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
