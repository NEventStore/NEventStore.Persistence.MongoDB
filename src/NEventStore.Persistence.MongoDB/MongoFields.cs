﻿namespace NEventStore.Persistence.MongoDB
{
    public static class MongoSystemBuckets
    {
        public const string RecycleBin = ":rb";
    }

    public static class MongoStreamHeadFields
    {
        public const string Id = "_id";
        public const string BucketId = "BucketId";
        public const string StreamId = "StreamId";
        public const string HeadRevision = "HeadRevision";
        public const string SnapshotRevision = "SnapshotRevision";
        public const string Unsnapshotted = "Unsnapshotted";
        public const string FullQualifiedBucketId = Id + "." + BucketId;
    }

    public static class MongoShapshotFields
    {
        public const string Id = "_id";
        public const string BucketId = "BucketId";
        public const string StreamId = "StreamId";
        public const string Payload = "Payload";
        public const string StreamRevision = "StreamRevision";
        public const string FullQualifiedBucketId = Id + "." + BucketId;
        public const string FullQualifiedStreamId = Id + "." + StreamId;
        public const string FullQualifiedStreamRevision = Id + "." + StreamRevision;
    }

    public static class MongoCommitFields
    {
        public const string CheckpointNumber = "_id";

        public const string BucketId = "BucketId";
        public const string StreamId = "StreamId";
        public const string StreamRevision = "StreamRevision";
        public const string StreamRevisionFrom = "StreamRevisionFrom";
        public const string StreamRevisionTo = "StreamRevisionTo";

        public const string CommitId = "CommitId";
        public const string CommitStamp = "CommitStamp";
        public const string CommitSequence = "CommitSequence";
        public const string Events = "Events";
        public const string Headers = "Headers";
        public const string Payload = "Payload";
    }

    public static class MongoCommitIndexes
    {
        /// <summary>
        /// the following value is used to determine the index
        /// that throws exception when a duplicate is found.
        /// </summary>
        public const string CheckpointNumberMMApV1 = "$_id_";
        public const string CheckpointNumberWiredTiger = "index: _id_";

        public const string CommitStamp = "CommitStamp_Index";
        public const string CommitId = "CommitId_Index";
        public const string GetFrom = "GetFrom_Index";
        public const string LogicalKey = "LogicalKey_Index";
        public const string GetFromCheckpoint = "GetFrom_Checkpoint_Index";
    }

    public static class MongoStreamIndexes
    {
        public const string Unsnapshotted = "Unsnapshotted_Index";
    }
}