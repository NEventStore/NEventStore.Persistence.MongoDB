using NEventStore.Persistence.MongoDB.Support;
using System;
using global::MongoDB.Driver;

namespace NEventStore.Persistence.MongoDB
{
    /// <summary>
    /// <para>Options for the MongoPersistence engine.</para>
    /// <para>
    /// links to check:
    /// https://www.mongodb.com/docs/drivers/csharp/current/fundamentals/connection/connect/#connection-guide
    /// https://www.mongodb.com/docs/drivers/csharp/current/faq/#how-does-connection-pooling-work-in-the-.net-c--driver-
    /// http://docs.mongodb.org/manual/core/write-concern/#write-concern
    /// </para>
    /// </summary>
    public class MongoPersistenceOptions
    {
        /// <summary>
        /// <para>
        /// The <see cref="IMongoClient"/> instance to use to connect to the Mongo server.
        /// According to MongoDB best practices, a single MongoClient instance should be used for the entire application.
        /// </para>
        /// <para>If you specify a MongoClient instance, the ConfigureClientSettingsAction will not be used.</para>
        /// </summary>
        public IMongoClient MongoClient { get; set; }

        /// <summary>
        /// A delegate to configure the <see cref="MongoClientSettings"/> used to connect to the Mongo server.
        /// A new MongoClient instance will be created for each MongoPersistenceEngine instance.
        /// </summary>
        public Action<MongoClientSettings> ConfigureClientSettingsAction { get; set; }

        /// <summary>
        /// Get the  <see href="http://docs.mongodb.org/manual/core/write-concern/#write-concern">WriteConcern</see> for the commit insert operation.
        /// Concurrency / duplicate commit detection require a safe mode so level should be at least Acknowledged
        /// </summary>
        /// <returns>the write concern for the commit insert operation</returns>
        public virtual WriteConcern GetInsertCommitWriteConcern()
        {
            // for concurrency / duplicate commit detection safe mode is required
            // minimum level is Acknowledged
            return WriteConcern.Acknowledged;
        }

        /// <summary>
        /// Get the MongoCollectionSettings for the Commits collection.
        /// </summary>
        public virtual MongoCollectionSettings GetCommitSettings()
        {
            return new MongoCollectionSettings
            {
                AssignIdOnInsert = false,
                WriteConcern = WriteConcern.Acknowledged
            };
        }

        /// <summary>
        /// Get the MongoCollectionSettings for the Snapshots collection.
        /// </summary>
        public virtual MongoCollectionSettings GetSnapshotSettings()
        {
            return new MongoCollectionSettings
            {
                AssignIdOnInsert = false,
                WriteConcern = WriteConcern.Unacknowledged
            };
        }

        /// <summary>
        /// Get the MongoCollectionSettings for the Streams collection.
        /// </summary>
        public virtual MongoCollectionSettings GetStreamSettings()
        {
            return new MongoCollectionSettings
            {
                AssignIdOnInsert = false,
                WriteConcern = WriteConcern.Unacknowledged
            };
        }

        private static readonly object _connectToDatabaseLock = new object();

        /// <summary>
        /// Connects to NEvenstore Mongo database
        /// </summary>
        /// <param name="connectionString">Connection string</param>
        /// <returns>nevenstore mongodatabase store</returns>
        public virtual IMongoDatabase ConnectToDatabase(string connectionString)
        {
            var builder = new MongoUrlBuilder(connectionString);
            // if there's a MongoClient instance, use it
            IMongoClient client = MongoClient;
            if (client == null)
            {
                var cacheKey = new MongoClientCache.MongoClientCacheKey(
                    connectionString,
                    ConfigureClientSettingsAction
                    );
                client = MongoClientCache.GetClient(cacheKey);
                if (client == null)
                {
                    lock (_connectToDatabaseLock)
                    {
                        client = MongoClientCache.GetClient(cacheKey);
                        if (client == null)
                        {
                            var clientSettings = MongoClientSettings.FromConnectionString(connectionString);
                            ConfigureClientSettingsAction?.Invoke(clientSettings);
                            client = new MongoClient(clientSettings);
                            MongoClientCache.TryAddClient(cacheKey, client);
                        }
                    }
                }
            }
            else
            {
                // check that the connection string matches the client settings
                var clientSettings = client.Settings;
                if (clientSettings.Server.Host != builder.Server.Host)
                {
                    throw new ArgumentException("MongoClient instance was created with a different connection string");
                }
            }
            return client.GetDatabase(builder.DatabaseName);
        }

        /// <summary>
        /// This is the instance of the Id Generator I want to use to
        /// generate checkpoint.
        /// </summary>
        public ICheckpointGenerator CheckpointGenerator { get; set; }

        /// <summary>
        /// The strategy to use when a concurrency exception is detected.
        /// </summary>
        public ConcurrencyExceptionStrategy ConcurrencyStrategy { get; set; }

        /// <summary>
        /// Name of the bucket that will contain the system streams.
        /// </summary>
        public String SystemBucketName { get; set; }

        /// <summary>
        /// Set this property to true to ask Persistence Engine to disable
        /// snapshot support. If you are not using snapshot functionalities
        /// this options allows you to save the extra insert to insert Stream Heads.
        /// </summary>
        /// <remarks>
        /// If you disable Stream Heads, you are not able to ask
        /// for stream that need to be snapshotted. Basically you should set
        /// this to true if you not use NEventstore snapshot functionalities.
        /// </remarks>
        public Boolean DisableSnapshotSupport { get; set; }

        /// <summary>
        /// The default behavior when using snapshot is to persist the stream heads in
        /// a background threads, but this way it can be hard to test if the heads and snapshots
        /// are computed an updated correctly after a commit.
        /// This setting is here mainly to help testing.
        /// </summary>
        public Boolean PersistStreamHeadsOnBackgroundThread { get; set; } = true;

        /// <summary>
        /// Creates an instance of the NEventStore MongoDB persistence configuration class.
        /// </summary>
        /// <param name="configureClientSettingsAction">
        /// Allows to customize Driver's specific client connection settings.
        /// The function should be static and thread safe, so it will
        /// allow for a correct caching of the generated <see cref="IMongoClient"/> instance.
        /// </param>
        /// <param name="mongoClient">
        /// The <see cref="IMongoClient"/> to use to connect to MongoDB database. 
        /// If specified <paramref name="configureClientSettingsAction"/> will be ignored.
        /// </param>
        public MongoPersistenceOptions(
            Action<MongoClientSettings> configureClientSettingsAction = null,
            IMongoClient mongoClient = null
            )
        {
            ConfigureClientSettingsAction = configureClientSettingsAction;
            MongoClient = mongoClient;
            SystemBucketName = "system";
            ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue;
        }
    }

    /// <summary>
    /// The strategy to use when a concurrency exception is detected.
    /// </summary>
    public enum ConcurrencyExceptionStrategy
    {
        /// <summary>
        /// When a <see cref="ConcurrencyException"/> is thrown, simply continue
        /// and ask to <see cref="ICheckpointGenerator"/> implementation new id.
        /// </summary>
        Continue = 0,

        /// <summary>
        /// When a <see cref="ConcurrencyException"/> is thrown, generate an empty
        /// commit with current Checkpoint, then ask to
        /// <see cref="ICheckpointGenerator"/> implementation new id.
        /// </summary>
        FillHole = 1,
    }
}