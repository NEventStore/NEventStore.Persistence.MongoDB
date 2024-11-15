using System;
using Microsoft.Extensions.Logging;
using NEventStore.Logging;
using NEventStore.Persistence.MongoDB;
using NEventStore.Serialization;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace NEventStore
#pragma warning restore IDE0130 // Namespace does not match folder structure
{
    /// <summary>
    /// Represents the persistence wire-up for MongoDB.
    /// </summary>
    public class MongoPersistenceWireup : PersistenceWireup
    {
        private static readonly ILogger Logger = LogFactory.BuildLogger(typeof(MongoPersistenceWireup));

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoPersistenceWireup"/> class.
        /// </summary>
        public MongoPersistenceWireup(Wireup inner, Func<string> connectionStringProvider, IDocumentSerializer serializer, MongoPersistenceOptions persistenceOptions)
            : base(inner)
        {
            Logger.LogDebug("Configuring Mongo persistence engine.");

            /* Transaction will be handled differently by each driver
            var options = Container.Resolve<TransactionScopeOption>();
            if (options != TransactionScopeOption.Suppress)
            {
                Logger.LogWarning("MongoDB does not participate in transactions using TransactionScope.");
            }
            */

            Container.Register(_ => new MongoPersistenceFactory(connectionStringProvider, serializer, persistenceOptions).Build());
        }
    }
}
