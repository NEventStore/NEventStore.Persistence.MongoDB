// ReSharper disable CheckNamespace

namespace NEventStore // ReSharper restore CheckNamespace
{
    using System;
    using System.Transactions;
    using Microsoft.Extensions.Logging;
    using NEventStore.Logging;
    using NEventStore.Persistence.MongoDB;
    using NEventStore.Serialization;

    public class MongoPersistenceWireup : PersistenceWireup
    {
        private static readonly ILogger Logger = LogFactory.BuildLogger(typeof(MongoPersistenceWireup));

        public MongoPersistenceWireup(Wireup inner, Func<string> connectionStringProvider, IDocumentSerializer serializer, MongoPersistenceOptions persistenceOptions)
            : base(inner)
        {
            Logger.LogDebug("Configuring Mongo persistence engine.");

            var options = Container.Resolve<TransactionScopeOption>();
            if (options != TransactionScopeOption.Suppress)
            {
                Logger.LogWarning("MongoDB does not participate in transactions using TransactionScope.");
            }

            Container.Register(_ => new MongoPersistenceFactory(connectionStringProvider, serializer, persistenceOptions).Build());
        }
    }
}
