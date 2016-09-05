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
        public const string Dispatched = "Dispatched";
        public const string Payload = "Payload";
    }

    public static class MongoCommitIndexes
    {
        public const string CheckpointNumber = "_id_";
        public const string CommitStamp = "CommitStamp_Index";
        public const string GetFrom = "GetFrom_Index";
        public const string Dispatched = "Dispatched_Index";
        public const string LogicalKey = "LogicalKey_Index";
    }

    public static class MongoStreamIndexes
    {
        public const string Unsnapshotted = "Unsnapshotted_Index";
    }
}