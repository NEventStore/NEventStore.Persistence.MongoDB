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
    public partial class MongoPersistenceWireup : PersistenceWireup
    {
        private static readonly ILogger Logger = LogFactory.BuildLogger(typeof(MongoPersistenceWireup));

        [LoggerMessage(EventId = 1100, Level = LogLevel.Debug, Message = "Configuring Mongo persistence engine.")]
        private static partial void ConfiguringMongoPersistenceEngineMessage(ILogger logger);

        [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "MongoDB does not participate in transactions using TransactionScope.")]
        private static partial void TransactionScopeWarningMessage(ILogger logger);

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoPersistenceWireup"/> class.
        /// </summary>
        public MongoPersistenceWireup(Wireup inner, Func<string> connectionStringProvider, IDocumentSerializer serializer, MongoPersistenceOptions? persistenceOptions)
            : base(inner)
        {
            ConfiguringMongoPersistenceEngineMessage(Logger);

            /* Transaction will be handled differently by each driver
            var options = Container.Resolve<TransactionScopeOption>();
            if (options != TransactionScopeOption.Suppress)
            {
                TransactionScopeWarningMessage(Logger);
            }
            */

            Container.Register(_ => new MongoPersistenceFactory(connectionStringProvider, serializer, persistenceOptions).Build());
        }
    }
}
