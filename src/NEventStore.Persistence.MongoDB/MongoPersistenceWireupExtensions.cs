#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace NEventStore
#pragma warning restore IDE0130 // Namespace does not match folder structure
{
    using System;
#if NET472_OR_GREATER
    using System.Configuration;
#endif
    using NEventStore.Persistence.MongoDB;
    using NEventStore.Serialization;

    /// <summary>
    /// Provides a set of extension methods to configure the MongoDB persistence engine.
    /// </summary>
    public static class MongoPersistenceWireupExtensions
    {
        // System.Configuration will not be ported to dotnet core
#if NET472_OR_GREATER
        /// <summary>
        /// Configures the persistence engine to use MongoDB.
        /// </summary>
        /// <exception cref="NEventStore.Persistence.MongoDB.ConfigurationException"></exception>
        public static PersistenceWireup UsingMongoPersistence(this Wireup wireup, string connectionName, IDocumentSerializer serializer, MongoPersistenceOptions options = null)
        {
            return new MongoPersistenceWireup(wireup, () =>
            {
                var connectionStringSettings = ConfigurationManager.ConnectionStrings[connectionName]
                    ?? throw new NEventStore.Persistence.MongoDB.ConfigurationException(Messages.ConnectionNotFound.FormatWith(connectionName));
                return connectionStringSettings.ConnectionString;
            }, serializer, options);
        }
#else
        /// <summary>
        /// Configures the persistence engine to use MongoDB.
        /// </summary>
        /// <exception cref="NEventStore.Persistence.MongoDB.ConfigurationException"></exception>
        public static PersistenceWireup UsingMongoPersistence(this Wireup wireup, string connectionString, IDocumentSerializer serializer, MongoPersistenceOptions options = null)
        {
            return new MongoPersistenceWireup(wireup, () =>
            {
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new NEventStore.Persistence.MongoDB.ConfigurationException(Messages.ConnectionNotFound.FormatWith(connectionString));

                return connectionString;
            }, serializer, options);
        }
#endif

        /// <summary>
        /// Configures the persistence engine to use MongoDB.
        /// </summary>
        public static PersistenceWireup UsingMongoPersistence(this Wireup wireup, Func<string> connectionStringProvider, IDocumentSerializer serializer, MongoPersistenceOptions options = null)
        {
            return new MongoPersistenceWireup(wireup, connectionStringProvider, serializer, options);
        }
    }
}