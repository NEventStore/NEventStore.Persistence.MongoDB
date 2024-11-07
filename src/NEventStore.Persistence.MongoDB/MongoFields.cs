namespace NEventStore.Persistence.MongoDB
{
    /// <summary>
    /// MongoDB Fields
    /// </summary>
    public static class MongoSystemBuckets
    {
        /// <summary>
        /// The Recycle Bin bucket identifier.
        /// </summary>
        public const string RecycleBin = ":rb";
    }

    /// <summary>
    /// MongoDB Fields
    /// </summary>
    public static class MongoStreamHeadFields
    {
        /// <summary>
        /// The ID field.
        /// </summary>
        public const string Id = "_id";
        /// <summary>
        /// The bucket identifier field.
        /// </summary>
        public const string BucketId = "BucketId";
        /// <summary>
        /// The stream identifier field.
        /// </summary>
        public const string StreamId = "StreamId";
        /// <summary>
        /// The head revision field.
        /// </summary>
        public const string HeadRevision = "HeadRevision";
        /// <summary>
        /// The snapshot revision field.
        /// </summary>
        public const string SnapshotRevision = "SnapshotRevision";
        /// <summary>
        /// The difference between the head and snapshot revision field.
        /// </summary>
        public const string Unsnapshotted = "Unsnapshotted";
        /// <summary>
        /// The full qualified bucket identifier.
        /// </summary>
        public const string FullQualifiedBucketId = Id + "." + BucketId;
    }

    /// <summary>
    /// MongoDB Snapshot Fields
    /// </summary>
    public static class MongoSnapshotFields
    {
        /// <summary>
        /// The ID field.
        /// </summary>
        public const string Id = "_id";
        /// <summary>
        /// The bucket identifier field.
        /// </summary>
        public const string BucketId = "BucketId";
        /// <summary>
        /// The stream identifier field.
        /// </summary>
        public const string StreamId = "StreamId";
        /// <summary>
        /// The payload field.
        /// </summary>
        public const string Payload = "Payload";
        /// <summary>
        /// The stream revision field.
        /// </summary>
        public const string StreamRevision = "StreamRevision";
        /// <summary>
        /// The full qualified bucket identifier.
        /// </summary>
        public const string FullQualifiedBucketId = Id + "." + BucketId;
        /// <summary>
        /// The full qualified stream identifier.
        /// </summary>
        public const string FullQualifiedStreamId = Id + "." + StreamId;
        /// <summary>
        /// The full qualified stream revision.
        /// </summary>
        public const string FullQualifiedStreamRevision = Id + "." + StreamRevision;
    }

    /// <summary>
    /// MongoDB Commit Fields
    /// </summary>
    public static class MongoCommitFields
    {
        /// <summary>
        /// The checkpoint number (id) field.
        /// </summary>
        public const string CheckpointNumber = "_id";

        /// <summary>
        /// The bucket identifier field.
        /// </summary>
        public const string BucketId = "BucketId";
        /// <summary>
        /// The stream identifier field.
        /// </summary>
        public const string StreamId = "StreamId";
        /// <summary>
        /// The stream revision field.
        /// </summary>
        public const string StreamRevision = "StreamRevision";
        /// <summary>
        /// The stream revision from field.
        /// </summary>
        public const string StreamRevisionFrom = "StreamRevisionFrom";
        /// <summary>
        /// The stream revision to field.
        /// </summary>
        public const string StreamRevisionTo = "StreamRevisionTo";

        /// <summary>
        /// The commit ID field.
        /// </summary>
        public const string CommitId = "CommitId";
        /// <summary>
        /// The commit timestamp field.
        /// </summary>
        public const string CommitStamp = "CommitStamp";
        /// <summary>
        /// The commit sequence field.
        /// </summary>
        public const string CommitSequence = "CommitSequence";
        /// <summary>
        /// Events field.
        /// </summary>
        public const string Events = "Events";
        /// <summary>
        /// Headers field.
        /// </summary>
        public const string Headers = "Headers";
        /// <summary>
        /// Payload field.
        /// </summary>
        public const string Payload = "Payload";
    }

    /// <summary>
    /// MongoDB Indexes
    /// </summary>
    public static class MongoCommitIndexes
    {
        /// <summary>
        /// the following value is used to determine the index
        /// that throws exception when a duplicate is found.
        /// </summary>
        public const string CheckpointNumberMMApV1 = "$_id_";
        /// <summary>
        /// the following value is used to determine the index
        /// that throws exception when a duplicate is found.
        /// </summary>
        public const string CheckpointNumberWiredTiger = "index: _id_";

        /// <summary>
        /// The commit stamp index.
        /// </summary>
        public const string CommitStamp = "CommitStamp_Index";
        /// <summary>
        /// The commit ID index.
        /// </summary>
        public const string CommitId = "CommitId_Index";
        /// <summary>
        /// The commit stamp get from index.
        /// </summary>
        public const string GetFrom = "GetFrom_Index";
        /// <summary>
        /// Logical unique key index.
        /// </summary>
        public const string LogicalKey = "LogicalKey_Index";
        /// <summary>
        /// The checkpoint get from index.
        /// </summary>
        public const string GetFromCheckpoint = "GetFrom_Checkpoint_Index";
    }

    /// <summary>
    /// MongoDB Stream Indexes
    /// </summary>
    public static class MongoStreamIndexes
    {
        /// <summary>
        /// Un-snapshotted index.
        /// </summary>
        public const string Unsnapshotted = "Unsnapshotted_Index";
    }
}