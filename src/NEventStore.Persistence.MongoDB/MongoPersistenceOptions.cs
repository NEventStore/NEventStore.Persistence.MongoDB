using MongoDB.Bson;
using NEventStore.Persistence.MongoDB.Support;

namespace NEventStore.Persistence.MongoDB
{
	using System;
	using global::MongoDB.Driver;
	using NEventStore.Serialization;

	/// <summary>
	/// Options for the MongoPersistence engine.
	/// http://docs.mongodb.org/manual/core/write-concern/#write-concern
	/// </summary>
	public  class MongoPersistenceOptions
	{
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

		public virtual MongoCollectionSettings GetCommitSettings()
		{
			return new MongoCollectionSettings
			{
				AssignIdOnInsert = false,
				WriteConcern = WriteConcern.Acknowledged
			};
		}

		public virtual MongoCollectionSettings GetSnapshotSettings()
		{
			return new MongoCollectionSettings
			{
				AssignIdOnInsert = false,
				WriteConcern = WriteConcern.Unacknowledged
			};
		}

		public virtual MongoCollectionSettings GetStreamSettings()
		{
			return new MongoCollectionSettings
			{
				AssignIdOnInsert = false,
				WriteConcern = WriteConcern.Unacknowledged
			};
		}

	    /// <summary>
		/// Connects to NEvenstore Mongo database
		/// </summary>
		/// <param name="connectionString">Connection string</param>
		/// <returns>nevenstore mongodatabase store</returns>
		public virtual IMongoDatabase ConnectToDatabase(string connectionString)
		{
			var builder = new MongoUrlBuilder(connectionString);
			var database = (new MongoClient(connectionString)).GetDatabase(builder.DatabaseName);
			return database;
		}

        /// <summary>
        /// This is the instance of the Id Generator I want to use to 
        /// generate checkpoint. 
        /// </summary>
	    public ICheckpointGenerator CheckpointGenerator { get; set; }

        public ConcurrencyExceptionStrategy ConcurrencyStrategy { get; set; }

        public String SystemBucketName { get; set; }

        public MongoPersistenceOptions()
	    {
            SystemBucketName = "system";
            ConcurrencyStrategy = ConcurrencyExceptionStrategy.Continue; 
	    }
	}

    public enum ConcurrencyExceptionStrategy
    {
        /// <summary>
        /// When a <see cref="ConcurrencyException"/> is thrown, simply continue
        /// and ask to <see cref="ICheckpointGenerator"/> implementation new id.
        /// </summary>
        Continue = 0,

        /// <summary>
        /// When a <see cref="ConcurrencyException"/> is thrown, generate an empty
        /// commit with current <see cref="LongCheckpoint"/>, then ask to 
        /// <see cref="ICheckpointGenerator"/> implementation new id.
        /// </summary>
        FillHole = 1,
    }
}
