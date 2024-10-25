namespace NEventStore.Persistence.MongoDB
{
    using System;
    using NEventStore.Serialization;

    /// <summary>
    /// Represents a factory for creating MongoDB persistence engines.
    /// </summary>
    public class MongoPersistenceFactory : IPersistenceFactory
    {
        private readonly Func<string> _connectionStringProvider;
        private readonly IDocumentSerializer _serializer;
        private readonly MongoPersistenceOptions _options;

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoPersistenceFactory"/> class.
        /// </summary>
        public MongoPersistenceFactory(Func<string> connectionStringProvider, IDocumentSerializer serializer, MongoPersistenceOptions options = null)
        {
            _connectionStringProvider = connectionStringProvider;
            _serializer = serializer;
            _options = options ?? new MongoPersistenceOptions();
        }

        /// <inheritdoc />
        public virtual IPersistStreams Build()
        {
            string connectionString = _connectionStringProvider();
            var database = _options.
                ConnectToDatabase(connectionString);
            return new MongoPersistenceEngine(database, _serializer, _options);
        }
    }
}
