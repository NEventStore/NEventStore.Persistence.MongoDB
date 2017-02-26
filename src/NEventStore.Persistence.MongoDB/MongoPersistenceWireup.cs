// ReSharper disable CheckNamespace

namespace NEventStore // ReSharper restore CheckNamespace
{
    using System;
#if !NETSTANDARD1_6
	using System.Transactions;
#endif
    using NEventStore.Logging;
    using NEventStore.Persistence.MongoDB;
    using NEventStore.Serialization;

    public class MongoPersistenceWireup : PersistenceWireup
    {
        private static readonly ILog Logger = LogFactory.BuildLogger(typeof (MongoPersistenceWireup));

        public MongoPersistenceWireup(Wireup inner, Func<string> connectionStringProvider, IDocumentSerializer serializer, MongoPersistenceOptions persistenceOptions)
            : base(inner)
        {
            Logger.Debug("Configuring Mongo persistence engine.");

#if !NETSTANDARD1_6
			var options = Container.Resolve<TransactionScopeOption>();
            if (options != TransactionScopeOption.Suppress)
            {
                Logger.Warn("MongoDB does not participate in transactions using TransactionScope.");
            }
#endif

			Container.Register(c => new MongoPersistenceFactory(connectionStringProvider, serializer, persistenceOptions).Build());
        }
    }
}
