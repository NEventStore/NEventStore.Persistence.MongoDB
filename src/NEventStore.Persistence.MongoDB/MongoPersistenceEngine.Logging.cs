using Microsoft.Extensions.Logging;

namespace NEventStore.Persistence.MongoDB
{
    internal static partial class MongoPersistenceEngineLogMessages
    {
        internal const string DuplicatedCommitErrorTemplate = "[NEventStore.Persistence.MongoDB] Duplicated commitId {0} [{1}] - Bucket {2} - StreamId {3}.";
        internal const string ConnectionNotFoundTemplate = "Could not find connection name '{0}' in the configuration file.";

        [LoggerMessage(EventId = 1000, Level = LogLevel.Debug, Message = "Initializing storage engine.")]
        internal static partial void InitializingStorage(ILogger logger);

        [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Getting all commits for stream '{StreamId}' in bucket '{BucketId}' between revisions '{MinRevision}' and '{MaxRevision}'.")]
        internal static partial void GettingAllCommitsBetween(ILogger logger, string streamId, string bucketId, int minRevision, int maxRevision);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Getting all commits from '{Start}' forward from bucket '{BucketId}'.")]
        internal static partial void GettingAllCommitsFrom(ILogger logger, DateTime start, string bucketId);

        [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Getting all commits from Bucket '{BucketId}' and checkpoint '{CheckpointToken}'.")]
        internal static partial void GettingAllCommitsFromBucketAndCheckpoint(ILogger logger, string bucketId, long checkpointToken);

        [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "Getting all commits from bucket '{BucketId}' from checkpoint '{FromCheckpointToken}' (excluded) up to '{ToCheckpointToken}' (included).")]
        internal static partial void GettingCommitsFromBucketAndFromToCheckpoint(ILogger logger, string bucketId, long fromCheckpointToken, long toCheckpointToken);

        [LoggerMessage(EventId = 1005, Level = LogLevel.Debug, Message = "Getting all commits since checkpoint '{CheckpointToken}'.")]
        internal static partial void GettingAllCommitsFromCheckpoint(ILogger logger, long checkpointToken);

        [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Getting all commits from checkpoint '{FromCheckpointToken}' (excluded) up to '{ToCheckpointToken}' (included).")]
        internal static partial void GettingCommitsFromToCheckpoint(ILogger logger, long fromCheckpointToken, long toCheckpointToken);

        [LoggerMessage(EventId = 1007, Level = LogLevel.Debug, Message = "Getting all commits from '{Start}' to '{End}'.")]
        internal static partial void GettingAllCommitsFromTo(ILogger logger, DateTime start, DateTime end);

        [LoggerMessage(EventId = 1008, Level = LogLevel.Debug, Message = "Attempting to commit {EventCount} events on stream '{StreamId}' at sequence {CommitSequence}.")]
        internal static partial void AttemptingToCommit(ILogger logger, int eventCount, string streamId, int commitSequence);

        [LoggerMessage(EventId = 1009, Level = LogLevel.Debug, Message = "Commit '{CommitId}' persisted.")]
        internal static partial void CommitPersisted(ILogger logger, Guid commitId);

        [LoggerMessage(EventId = 1010, Level = LogLevel.Error, Message = "Generic error persisting commit {CommitId} [{CheckpointId}] - Bucket {BucketId} - StreamId {StreamId} - Ex: {ExceptionText}.")]
        internal static partial void GenericPersistingError(ILogger logger, Guid commitId, long checkpointId, string bucketId, string streamId, string exceptionText, Exception exception);

        [LoggerMessage(EventId = 1011, Level = LogLevel.Warning, Message = "Duplicated checkpoint Token commit {CommitId} [{CheckpointId}] - Bucket {BucketId} - StreamId {StreamId}.")]
        internal static partial void DuplicatedCheckpointTokenError(ILogger logger, Guid commitId, long checkpointId, string bucketId, string streamId, Exception exception);

        [LoggerMessage(EventId = 1012, Level = LogLevel.Information, Message = "{Message}")]
        internal static partial void Information(ILogger logger, string message);

        [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "Concurrency Exception commitId {CommitId} [{CheckpointId}] - Bucket {BucketId} - StreamId {StreamId}.")]
        internal static partial void ConcurrencyExceptionError(ILogger logger, Guid commitId, long checkpointId, string bucketId, string streamId, Exception exception);

        [LoggerMessage(EventId = 1014, Level = LogLevel.Warning, Message = "Error filling hole commitId {CommitId} [{CheckpointId}] - Bucket {BucketId} - StreamId {StreamId}.")]
        internal static partial void FillHoleError(ILogger logger, Guid commitId, long checkpointId, string bucketId, string streamId, Exception exception);

        [LoggerMessage(EventId = 1015, Level = LogLevel.Debug, Message = "Getting a list of streams to snapshot.")]
        internal static partial void GettingStreamsToSnapshot(ILogger logger);

        [LoggerMessage(EventId = 1016, Level = LogLevel.Debug, Message = "Getting snapshot for stream '{StreamId}' on or before revision {MaxRevision}.")]
        internal static partial void GettingRevision(ILogger logger, string streamId, int maxRevision);

        [LoggerMessage(EventId = 1017, Level = LogLevel.Debug, Message = "Adding snapshot to stream '{StreamId}' in bucket '{BucketId}' at position {StreamRevision}.")]
        internal static partial void AddingSnapshot(ILogger logger, string streamId, string bucketId, int streamRevision);

        [LoggerMessage(EventId = 1018, Level = LogLevel.Warning, Message = "Error Adding snapshot to stream '{StreamId}' in bucket '{BucketId}' at position {StreamRevision}.")]
        internal static partial void AddingSnapshotError(ILogger logger, string streamId, string bucketId, int streamRevision, Exception exception);

        [LoggerMessage(EventId = 1019, Level = LogLevel.Warning, Message = "Purging all stored data.")]
        internal static partial void PurgingStorage(ILogger logger);

        [LoggerMessage(EventId = 1020, Level = LogLevel.Warning, Message = "Purging all stored data for bucket '{BucketId}'.")]
        internal static partial void PurgingBucket(ILogger logger, string bucketId);

        [LoggerMessage(EventId = 1021, Level = LogLevel.Warning, Message = "Deleting stream '{StreamId}' from bucket '{BucketId}'.")]
        internal static partial void DeletingStream(ILogger logger, string streamId, string bucketId);

        [LoggerMessage(EventId = 1022, Level = LogLevel.Debug, Message = "Shutting down persistence.")]
        internal static partial void ShuttingDownPersistence(ILogger logger);

        [LoggerMessage(EventId = 1023, Level = LogLevel.Error, Message = "OutOfMemoryException:")]
        internal static partial void OutOfMemoryException(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1024, Level = LogLevel.Warning, Message = "Ignored Exception when upserting the stream head Bucket Id [{BucketId}] StreamId[{StreamId}].\n")]
        internal static partial void IgnoredStreamHeadUpsertException(ILogger logger, string bucketId, string streamId, Exception exception);

        [LoggerMessage(EventId = 1025, Level = LogLevel.Warning, Message = "Storage is unavailabe.")]
        internal static partial void StorageUnavailable(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1026, Level = LogLevel.Error, Message = "Storage threw exception.")]
        internal static partial void StorageThrewException(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 1027, Level = LogLevel.Warning, Message = "Task was cancelled.")]
        internal static partial void TaskWasCancelled(ILogger logger, Exception exception);
    }
}
