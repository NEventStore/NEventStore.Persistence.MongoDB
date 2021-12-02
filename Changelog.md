# NEventStore.Persistence.MongoDB

## 9.0.0

- Updated NEventStore 9.0.0.
- Added support for net6.0.
- Updated MongoDB driver to 2.14.0.
- Configuration: allow to configure MongoClientSettings to edit driver specific client connection settings [#60](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/60).
- Added a new [BucketId, CheckpointNumber] index to speed up some queries.

### Breaking Change

- Minimum server version is now MongoDB 3.6+ (due to MongoDB C# driver update).

## 8.0.0

- Updated NEventStore to 8.0.0.
- Added support for net5.0, net461.
- Updated MongoDB driver to 2.11.x.

### Breaking Changes

- dropped net45 support.

## 7.0.0

- Updated NEventStore to 7.0.0.
- Updated the Persistence.Engine to implement new IPersistsStreams.GetFromTo(Int64, Int64) and IPersistsStreams.GetFromTo(String, Int64, Int64) interface methods.
- Optimized the query construction to remove edge cases from GetFrom methods (0, Int.MinValue. Int.MaxValue, DateTime.MinValue, DateTime.MaxValue)

## 6.0.0

__Version 6.x is not backwards compatible with version 5.x.__ Updating to NEventStore 6.x without doing some preparation work will result in problems.

Please read all the previous 6.x release notes.

## 6.0.0-rc-0

__Version 6.x is not backwards compatible with version 5.x.__ Updating to NEventStore 6.x without doing some preparation work will result in problems.

### New Features

- .Net Standard 2.0 / .Net Core 2.0 support.

### Breaking Changes

- **Removed Dispatcher and dispatching mechanic**: the Dispatched field in your previous commits is now meaningless, so is the Index associated with it.
- `ConfigurationErrorsException` has been replaced by `ConfigurationException`
- Added support for Id Generation done from client.
- Removed LongCheckpoint.
- Removed ServerSideLoop to generate CheckpointId.

### Manual upgrade operations:

- remove the `Dispatched` field from all your Commits in the Commits collection.
- remove the `Dispatched_Index` index from the Commits collection.

## 5.2.0

- Add support for IPersistStreams.GetFrom(BucketId, CheckpointToken) 
https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/25


## 5.0.1

- Added an unique index "LogicalKey_Index" on BucketId + StreamId + CommitSequence.

  To check the commits collection before updating for duplicates run this aggregation:

      db.Commits.aggregate([
        {$project : { _id: 1, BucketId:1, StreamId:1, CommitSequence:1}},
        {$group : { _id : { BucketId: "$BucketId", StreamId: "$StreamId", Seq :     "$CommitSequence"}, count : {$sum : 1}, checkpoints : {$push : "$_id"}}},
        {$match : {count : {$gt:1}}}
      ])

## 5.0.0.94

- changed the sort on GetFrom to avoid ScanAndOrder in memory on Commits collection
