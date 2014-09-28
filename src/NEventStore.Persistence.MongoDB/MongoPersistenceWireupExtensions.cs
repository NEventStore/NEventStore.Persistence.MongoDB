// ReSharper disable once CheckNamespace
namespace NEventStore
{
	using System;
	using System.Configuration;
	using NEventStore.Persistence.MongoDB;
	using NEventStore.Serialization;

	public static class MongoPersistenceWireupExtensions
	{
		public static PersistenceWireup UsingMongoPersistence(this Wireup wireup, string connectionName, IDocumentSerializer serializer, MongoPersistenceOptions options = null)
		{
			return new MongoPersistenceWireup(wireup, () =>
			{
			    var connectionStringSettings = ConfigurationManager.ConnectionStrings[connectionName];
                if( connectionStringSettings == null)
                    throw new ConfigurationErrorsException(Messages.ConnectionNotFound.FormatWith(connectionName));

			    return connectionStringSettings.ConnectionString;
			}, serializer, options);
		}

		public static PersistenceWireup UsingMongoPersistence(this Wireup wireup, Func<string> connectionStringProvider, IDocumentSerializer serializer, MongoPersistenceOptions options = null)
		{
			return new MongoPersistenceWireup(wireup, connectionStringProvider, serializer, options);
		}
	}
}