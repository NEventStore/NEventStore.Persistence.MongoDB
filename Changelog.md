# NEventStore.Persistence.MongoDB

## 11.0.0

- Support: net6.0, netstandard2.1, net472
- Updated MongoDb driver to 3.0.0
- Updated nuget package icon.

### Breaking Changes

- Carefully read the [MongoDB C# Driver 3.0 Migration Guide](https://www.mongodb.com/docs/drivers/csharp/v3.0/upgrade/v3/)
- dropped netstandard2.0 support.
- dropped net461 support.
- Removed Obsolete Extension methods: `ExtensionMethods.ToMongoCommit_original()`, `ExtensionMethods.ToCommit_original()`, `ExtensionMethods.AsDictionary<Tkey, Tvalue>()`, `ExtensionMethods.ToMongoCommitIdQuery()`
- Check your GUID serialization:
  - if it's a new project, you should not have any problem; you'll use the new GUID serialization format.
  - if it's an old project, you should check the GUID serialization format:
    - if you are using the `Standard` format, you should not have any problem.
    - if you are (most likely) using the `CSharpLegacy` format, you should change the GUID serialization format to `CSharpLegacy`:
      ```csharp
      BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
      ```
      or:
      ```csharp
      BsonClassMap.RegisterClassMap<MongoCommit>(cm =>
      {
        cm.AutoMap();
        cm.GetMemberMap(c => c.CommitId).SetSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
      });
      ```
      see README.md for more information.
- class `MongoShapshotFields` renamed to: `MongoSnapshotFields`

## 10.0.1

- Limit MongoDb allowed versions from 2.28.0 to anything less than 3.0.0 (which has many breaking changes to take care of).
- Added NuGet Symbol package generation.

## 10.0.0

- Updated MongoDB drivers to 2.28.0

### Breaking Changes

- MongoDB drivers are now strongly signed, binary compatibility with previous versions is now broken. If you update to this version of NEventStore.Persistence.MongoDB package
  you are forced to use the same version of MongoDB drivers (or setup assembly binding redirects).

## 9.1.2

- Fixed connectionString validation when connecting to a replica set using an already built MongoClient instance.

## 9.1.1

- Target Frameworks supported: netstandard2.0, net462
- Updated MongoDB driver to 2.20.0
- MongoDB initialization changed: MongoPersistenceOptions allows to specify a MongoClient instance to connect to MongoDB;
  due to [MongoDB Connection Guide](https://www.mongodb.com/docs/drivers/csharp/current/fundamentals/connection/connect/#connection-guide)
  and [How Does Connection Pooling Work in the .NET/C# Driver?](https://www.mongodb.com/docs/drivers/csharp/current/faq/#how-does-connection-pooling-work-in-the-.net-c--driver-)
  we should have just one instance of MongoClient per application.
  If we let the deriver to creates its own instance of MongoClient, that instance will be cached.
- Fix: Log exceptions from AddSnapshot [#63](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/63)

## 9.0.1

- Updated NEventStore reference to version 9.0.1
- Added documentation files to NuGet packages (improved intellisense support) [#61](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/61)

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
