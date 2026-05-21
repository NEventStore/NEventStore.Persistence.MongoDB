param(
    [string]$ContainerName = "nesci-mongo-1",
    [string]$DatabaseName = "issue73_explain",
    [switch]$KeepDatabase
)

$dropDatabaseStatement = if ($KeepDatabase) {
    ""
}
else {
    "db.dropDatabase();"
}

$script = @'
const dbName = "__DATABASE_NAME__";
const db = db.getSiblingDB(dbName);
__DROP_DATABASE__

const commits = db.getCollection("Commits");
const streamHeads = db.getCollection("Streams");
const snapshots = db.getCollection("Snapshots");

commits.createIndex({ BucketId: 1, _id: 1 }, { name: "GetFrom_Checkpoint_Index", unique: true });
commits.createIndex({ BucketId: 1, StreamId: 1, StreamRevisionFrom: 1, StreamRevisionTo: 1 }, { name: "GetFrom_Index", unique: true });
commits.createIndex({ BucketId: 1, StreamId: 1, CommitSequence: 1 }, { name: "LogicalKey_Index", unique: true });
commits.createIndex({ CommitStamp: 1 }, { name: "CommitStamp_Index" });
commits.createIndex({ BucketId: 1, StreamId: 1, CommitId: 1 }, { name: "CommitId_Index", unique: true });

snapshots.createIndex({ "_id.BucketId": 1, "_id.StreamId": 1, "_id.StreamRevision": -1 }, { name: "BucketStreamRevision_Index" });
streamHeads.createIndex({ Unsnapshotted: 1 }, { name: "Unsnapshotted_Index" });
streamHeads.createIndex({ "_id.BucketId": 1, Unsnapshotted: -1 }, { name: "BucketUnsnapshotted_Index" });

for (let i = 0; i < 20; i++) {
  const bucketId = i < 16 ? "default" : (i < 18 ? "other" : ":rb");
  const streamId = i < 8 ? "stream-1" : `stream-${i}`;
  const baseRevision = (i * 2) + 1;
  commits.insertOne({
    _id: i + 1,
    BucketId: bucketId,
    StreamId: streamId,
    StreamRevisionFrom: baseRevision,
    StreamRevisionTo: baseRevision + 1,
    CommitSequence: i + 1,
    CommitId: `commit-${i + 1}`,
    CommitStamp: new Date(Date.now() + i * 1000),
    Headers: {},
    Events: []
  });
}

streamHeads.insertMany([
  { _id: { BucketId: "default", StreamId: "stream-1" }, HeadRevision: 8, SnapshotRevision: 2, Unsnapshotted: 6 },
  { _id: { BucketId: "default", StreamId: "stream-2" }, HeadRevision: 4, SnapshotRevision: 0, Unsnapshotted: 4 },
  { _id: { BucketId: "other", StreamId: "stream-1" }, HeadRevision: 9, SnapshotRevision: 0, Unsnapshotted: 9 }
]);

snapshots.insertMany([
  { _id: { BucketId: "default", StreamId: "stream-1", StreamRevision: 1 }, Payload: "s1-r1" },
  { _id: { BucketId: "default", StreamId: "stream-1", StreamRevision: 3 }, Payload: "s1-r3" },
  { _id: { BucketId: "default", StreamId: "stream-1", StreamRevision: 5 }, Payload: "s1-r5" },
  { _id: { BucketId: "default", StreamId: "stream-2", StreamRevision: 2 }, Payload: "s2-r2" },
  { _id: { BucketId: "other", StreamId: "stream-1", StreamRevision: 4 }, Payload: "other-r4" }
]);

function summarizePlan(explain) {
  const stages = [];
  const indexNames = [];

  function walk(node) {
    if (!node || typeof node !== "object") return;
    if (node.stage) stages.push(node.stage);
    if (node.indexName) indexNames.push(node.indexName);
    for (const value of Object.values(node)) {
      if (Array.isArray(value)) {
        for (const item of value) walk(item);
      } else if (value && typeof value === "object") {
        walk(value);
      }
    }
  }

  walk(explain.queryPlanner?.winningPlan);
  return {
    winningStages: [...new Set(stages)],
    indexes: [...new Set(indexNames)],
    totalKeysExamined: explain.executionStats?.totalKeysExamined,
    totalDocsExamined: explain.executionStats?.totalDocsExamined,
    nReturned: explain.executionStats?.nReturned
  };
}

const results = {
  commitRangeRead: summarizePlan(
    commits.find({
      BucketId: "default",
      StreamId: "stream-1",
      StreamRevisionTo: { $gte: 3 },
      StreamRevisionFrom: { $lte: 6 }
    }).sort({ StreamRevisionFrom: 1 }).explain("executionStats")
  ),
  bucketDateRead: summarizePlan(
    commits.find({
      BucketId: "default",
      CommitStamp: { $gte: new Date(Date.now() - 1000) }
    }).sort({ _id: 1 }).explain("executionStats")
  ),
  bucketDateRangeRead: summarizePlan(
    commits.find({
      BucketId: "default",
      CommitStamp: {
        $gte: new Date(Date.now() - 1000),
        $lt: new Date(Date.now() + 60000)
      }
    }).sort({ _id: 1 }).explain("executionStats")
  ),
  bucketCheckpointRead: summarizePlan(
    commits.find({
      BucketId: "default",
      _id: { $gt: 3 }
    }).sort({ _id: 1 }).explain("executionStats")
  ),
  bucketCheckpointRangeRead: summarizePlan(
    commits.find({
      BucketId: "default",
      _id: { $gt: 3, $lte: 12 }
    }).sort({ _id: 1 }).explain("executionStats")
  ),
  allBucketsCheckpointRead: summarizePlan(
    commits.find({
      BucketId: { $ne: ":rb" },
      _id: { $gt: 3 }
    }).sort({ _id: 1 }).explain("executionStats")
  ),
  allBucketsCheckpointRangeRead: summarizePlan(
    commits.find({
      BucketId: { $ne: ":rb" },
      _id: { $gt: 3, $lte: 12 }
    }).sort({ _id: 1 }).explain("executionStats")
  ),
  duplicateCommitLookup: summarizePlan(
    commits.find({
      BucketId: "default",
      StreamId: "stream-1",
      CommitId: "commit-1"
    }).explain("executionStats")
  ),
  streamsToSnapshot: summarizePlan(
    streamHeads.find({
      "_id.BucketId": "default",
      Unsnapshotted: { $gte: 0 }
    }).sort({ Unsnapshotted: -1 }).explain("executionStats")
  ),
  getSnapshot: summarizePlan(
    snapshots.find({
      "_id.BucketId": "default",
      "_id.StreamId": "stream-1",
      "_id.StreamRevision": { $lte: 6 }
    }).sort({ "_id.StreamRevision": -1 }).limit(1).explain("executionStats")
  ),
  addSnapshotById: summarizePlan(
    snapshots.find({
      _id: { BucketId: "default", StreamId: "stream-1", StreamRevision: 3 }
    }).limit(1).explain("executionStats")
  ),
  addSnapshotStreamHeadLookup: summarizePlan(
    streamHeads.find({
      _id: { BucketId: "default", StreamId: "stream-1" }
    }).limit(1).explain("executionStats")
  ),
  purgeBucketCommitsFilter: summarizePlan(
    commits.find({
      BucketId: "default"
    }).explain("executionStats")
  ),
  purgeBucketSnapshotsFilter: summarizePlan(
    snapshots.find({
      "_id.BucketId": "default"
    }).explain("executionStats")
  ),
  purgeBucketStreamHeadsFilter: summarizePlan(
    streamHeads.find({
      "_id.BucketId": "default"
    }).explain("executionStats")
  ),
  deleteStreamHeadById: summarizePlan(
    streamHeads.find({
      _id: { BucketId: "default", StreamId: "stream-1" }
    }).explain("executionStats")
  ),
  deleteStreamSnapshotsFilter: summarizePlan(
    snapshots.find({
      "_id.BucketId": "default",
      "_id.StreamId": "stream-1"
    }).explain("executionStats")
  ),
  deleteStreamCommitsFilter: summarizePlan(
    commits.find({
      BucketId: "default",
      StreamId: "stream-1"
    }).explain("executionStats")
  ),
  updateStreamHeadById: summarizePlan(
    streamHeads.find({
      _id: { BucketId: "default", StreamId: "stream-1" }
    }).explain("executionStats")
  ),
  lastCommittedCheckpoint: summarizePlan(
    commits.find({}).sort({ _id: -1 }).limit(1).explain("executionStats")
  ),
  emptyRecycleBin: summarizePlan(
    commits.find({
      BucketId: ":rb",
      _id: { $lt: 20 }
    }).explain("executionStats")
  ),
  getDeletedCommits: summarizePlan(
    commits.find({
      BucketId: ":rb"
    }).sort({ _id: 1 }).explain("executionStats")
  )
};

print(JSON.stringify(results, null, 2));
'@

$script = $script.Replace('__DATABASE_NAME__', $DatabaseName).Replace('__DROP_DATABASE__', $dropDatabaseStatement)
$script | rtk docker exec -i $ContainerName mongosh --quiet