============================================
Change Log
============================================

5.0.1
- Added an unique index "LogicalKey_Index" on BucketId + StreamId + CommitSequence
  To check the commits collection before updating for duplicates run this aggregation
  db.Commits.aggregate([
    {$project : { _id: 1, BucketId:1, StreamId:1, CommitSequence:1}},
    {$group : { _id : { BucketId: "$BucketId", StreamId: "$StreamId", Seq : "$CommitSequence"}, count : {$sum : 1}, checkpoints : {$push : "$_id"}}},
    {$match : {count : {$gt:1}}}
  ])

5.0.0.94
- changed the sort on GetFrom to avoid ScanAndOrder in memory on Commits collection
