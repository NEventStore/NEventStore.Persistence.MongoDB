namespace NEventStore.Persistence.MongoDB.Tests
{
    using System;
    using NEventStore.Serialization;
    using global::MongoDB.Driver;

    public class AcceptanceTestMongoPersistenceFactory : MongoPersistenceFactory
    {
        private const string EnvVarConnectionStringKey = "NEventStore.MongoDB";

        public AcceptanceTestMongoPersistenceFactory(MongoPersistenceOptions options = null)
            : base(
                GetConnectionString,
                new DocumentObjectSerializer(),
                options ?? new MongoPersistenceOptions()
            )
        { }

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

            connectionString = connectionString.TrimStart('"').TrimEnd('"');

#if MSTEST
            return connectionString;

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
