# Performance Investigation

This document captures the current performance state of `NEventStore.Persistence.MongoDB` before any engine changes.

## Scope

- Inspect the MongoDB persistence engine hot paths.
- Validate the current benchmark suite.
- Record the first set of evidence-backed optimization targets.
- Defer runtime code changes until the benchmark harness can produce trustworthy baselines.

## Current Benchmark Status

The benchmark harness is now runnable and selectable from a single entrypoint.

### Baseline Profile (Before Snapshot)

Use this baseline profile for all future optimization comparisons:

- Runtime: `.NET 10.0` only (`net10.0` benchmark binary).
- Connection: `NEventStore.MongoDB=mongodb://localhost:50002/NEventStore`.
- Benchmark job: class-defined default (`Job-AGHMZC`, `LaunchCount=3`, `WarmupCount=3`, `IterationCount=3`, `InvocationCount=1`).
- Artifacts folder: `BenchmarkDotNet.Artifacts/results/`.

Baseline generation commands:

```powershell
dotnet build .\src\NEventStore.Persistence.MongoDB.Benchmark\NEventStore.Persistence.MongoDB.Benchmark.csproj -c Release -f net10.0
[Environment]::SetEnvironmentVariable('NEventStore.MongoDB', 'mongodb://localhost:50002/NEventStore', 'Process')

# Run full suite (net10)
dotnet .\src\NEventStore.Persistence.MongoDB.Benchmark\bin\Release\net10.0\NEventStore.Persistence.MongoDB.Benchmark.dll --filter *
```

### Commands used

```powershell
[Environment]::SetEnvironmentVariable('NEventStore.MongoDB', 'mongodb://localhost:50002/NEventStore', 'Process')
```

### Relevant evidence in the codebase

- Acceptance tests register the required MongoDB serializers in `src/NEventStore.Persistence.MongoDB.Tests/AcceptanceTestMongoPersistenceFactory.cs`.
- The benchmark helper in `src/NEventStore.Persistence.MongoDB.Benchmark/Support/EventStoreHelpers.cs` now mirrors that serializer setup and allows benchmark-specific `MongoPersistenceOptions`.
- The benchmark entrypoint in `src/NEventStore.Persistence.MongoDB.Benchmark/Program.cs` now uses `BenchmarkSwitcher.FromAssembly(...)`.

## Benchmark Suite Gaps

### Structural gaps

- No structural benchmark-entrypoint gaps remain for sync/async coverage currently implemented.

### Coverage gaps

The suite now includes these slices:

- Commit throughput with checkpoint generator comparison (`Always` vs `InMemory`).
- Per-stream read throughput (full stream and focused revision windows).
- Global checkpoint scans (bucket-qualified and all-buckets).
- Async global-read path and async commit path.
- Snapshot-related write overhead via `DisableSnapshotSupport` and `PersistStreamHeadsOnBackgroundThread` combinations.
- Delete and recycle-bin read behavior.
- Duplicate-commit and duplicate-checkpoint retry paths.

## High-Value Performance Findings

These are the strongest candidates for meaningful improvement, ordered by read-side first (higher leverage), then write-side findings. Confidence levels reflect data from benchmarks and index analysis.

### 1. Stream read queries need index optimization for checkpoint sort

**Read-side critical.** Per-stream reads filter on `(BucketId, StreamId, StreamRevisionFrom, StreamRevisionTo)` but sort by `CheckpointNumber`.

Current index structure:

```
INDEX: (BucketId, StreamId, StreamRevisionFrom, StreamRevisionTo)
QUERY FILTER: BucketId = X, StreamId = Y, revision range
QUERY SORT: CheckpointNumber ASC
```

Why this matters:

- The index supports the filter but NOT the sort (CheckpointNumber is not in the index).
- MongoDB must perform an in-memory sort after filtering, which is expensive for large streams.
- Checkpoint sort is correct and necessary for consistency—it's the logical clock across all events.
- Large aggregate reconstruction (many commits per stream) will hit this bottleneck directly.

What needs investigation:

- Validate index plan shape with MongoDB explain() for large and small streams.
- Measure sort overhead (in-memory sort cost vs index-covered sort).
- Consider extending the index to include CheckpointNumber for fully-covered sorts: `(BucketId, StreamId, StreamRevisionFrom, StreamRevisionTo, CheckpointNumber)`.
- Benchmark before/after adding checkpoint to index.

### 2. Read paths fully deserialize commits and all event payloads eagerly

**Read-side high-impact.** Every `GetFrom*` path ends in `doc.ToCommit(_serializer)`.

`ToCommit` currently does all of the following for every document:

- Deserializes the whole `MongoCommit` object from `BsonDocument`.
- Iterates every stored event.
- Deserializes each `EventMessage` payload.
- Materializes the event set into an array.

Why this matters:

- Global checkpoint scans pay full payload materialization cost even when the caller only needs iteration or metadata.
- This cost grows with event count and payload size.
- Allocation pressure will likely dominate long sequential reads (especially for large event counts per commit).
- This is the primary bottleneck for read-heavy workloads.

What needs benchmarking first:

- Small vs large payloads.
- Small vs large event counts per commit (1 event vs 100 events vs 1000 events per commit).
- Global reads vs per-stream reads (to isolate the effect).

### 3. All-buckets checkpoint reads use inefficient filter

**Read-side secondary.** The all-buckets checkpoint scan uses `BucketId != :rb` to exclude recycled streams.

Why this matters:

- Negative filters (`!=`) are less index-friendly than positive inclusion.
- Bucket-qualified reads use the primary checkpoint index; all-buckets reads may bypass it.
- Small performance cost, but measurable for high-volume checkpoint polling.

MongoDB explain check on the current query shape:

- `GetFrom(Int64 checkpointToken)` and `GetFromTo(long fromCheckpointToken, long toCheckpointToken)` both plan as `FETCH + IXSCAN`.
- The winning index for the all-buckets query is `_id_`, not `GetFrom_Checkpoint_Index`.
- That means this is **not** a COLLSCAN regression, and it is also **not** a missing-index problem.
- The gap is the negative filter shape preventing MongoDB from using the bucket-qualified checkpoint index.
- An explicit inclusion rewrite (`BucketId in [active buckets]`) did not change the winning plan in the checked data shape; MongoDB still chose `_id_`.
- A dedicated partial `_id` index is not a viable shortcut here because MongoDB rejects `partialFilterExpression` on `_id`.

What needs benchmarking first:

- Bucket-qualified vs all-buckets checkpoint reads (already in benchmark suite).
- Index explain() plan comparison between the two approaches.
- If this remains worth pursuing, test a query rewrite that actually changes scan order, not just the bucket predicate form.

### 4. Default checkpoint generation adds a database read per commit

**Write-side secondary.** `MongoPersistenceEngine.Initialize()` defaults to `AlwaysQueryDbForNextValueCheckpointGenerator`.

That generator calls `GetLastValue()` on every `Next()` invocation, which means one extra database read for every commit before the insert even happens.

Why this matters:

- Write-heavy workloads will pay an extra round trip per commit.
- However, read-side deserialization cost typically dominates for mixed workloads.
- The cost scales directly with commit volume.

What needs benchmarking first:

- Current default generator vs `InMemoryCheckpointGenerator` behavior (already in benchmark suite).
- Confirm this is secondary to read-path overhead.

### 5. Commit success path re-deserializes the inserted document

**Write-side lower-priority.** After a successful insert, `Commit()` returns `commitDoc.ToCommit(_serializer)`.

That means the write path serializes the commit to BSON for insert, then immediately deserializes the same BSON back to `ICommit` to return it.

Why this matters:

- Pure post-insert CPU and allocation overhead.
- The cost is paid on every successful commit, but write insertion is typically not the loop bottleneck.
- It is independent of MongoDB round-trip latency.

What needs benchmarking first:

- Compare this cost in the context of total write-path time (it may be noise).

### 6. Stream-head updates schedule background work per commit

**Write-side maintenance.** When snapshot support is enabled, successful commits call `UpdateStreamHeadInBackgroundThread(...)`.

On the sync path this uses `Task.Run` per commit when background updates are enabled.

Why this matters:

- Task-scheduling overhead on high write rates.
- Fire-and-forget work can add GC pressure and tail latency.
- This cost is isolated in the snapshot-overhead benchmark slices.

What needs benchmarking first:

- Snapshot support enabled vs disabled (already in benchmark suite).
- Background updates on/off comparison.

## Lower-Priority Findings

These are worth monitoring, but they are significantly lower-leverage than the findings above:

- Logging warnings from CA1848 suggest `LoggerMessage` could reduce hot-path logging overhead, but database and serialization work should be measured first.
- `AddSnapshot()` performs an extra stream-head read after snapshot upsert; this matters only for snapshot-heavy workloads and currently lacks deep benchmark coverage.

## Recommended Benchmark Work Before Engine Changes

The next performance pass should start by improving the benchmark harness, not the engine.

### Priority 0: Make the current suite runnable

- Completed: serializer registration and startup wiring fixed in benchmark setup.

### Priority 1: Make the suite selectable from the command line

- Completed: `Program.cs` now uses `BenchmarkSwitcher` and supports class/method filtering from CLI.

### Priority 2: Add focused benchmark slices

- Completed:
	- Write throughput: default checkpoint generator vs in-memory checkpoint generator.
	- Stream read throughput: full-stream reads plus focused revision-window reads.
	- Global read throughput: bucket-qualified and all-buckets checkpoint scans.
	- Snapshot overhead: snapshot support on/off and stream-head background updates on/off.
	- Async parity: async read and async commit scenarios added.
	- Delete/recycle-bin behavior and duplicate conflict/retry paths.

## Recommended Investigation Order Once Benchmarks Are Healthy

The next performance pass should start with **read-side optimization**, which is where the largest improvements likely exist.

1. Capture a real baseline (benchmark harness already ready).
2. **Validate stream-read index strategy** with MongoDB explain() plans—confirm whether checkpoint sort requires in-memory sort and whether adding CheckpointNumber to the index would help.
3. **Benchmark stream-read before/after** extended index (if justified by explain output).
4. **Measure deserialization cost** for read paths—isolate allocation pressure from payloads and event counts.
5. Only then revisit write-path optimizations (checkpoint generation and post-insert deserialization).

## Artifacts From This Investigation

BenchmarkDotNet outputs reports under `BenchmarkDotNet.Artifacts/results/` for each selected benchmark class.

With the current harness, those artifacts are suitable for baseline capture prior to engine optimizations.

### Before Snapshot (Net10)

The canonical "before" snapshot is the set of these net10 report files:

- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.CheckpointGeneratorBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.DuplicateConflictBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.GlobalCheckpointReadBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.ReadFromEventStoreAsyncBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.ReadFromEventStoreBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.ReadFromStreamBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.RecycleBinReadBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.SnapshotOverheadBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.StreamRevisionWindowBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.WriteToStreamAsyncBenchmarks-report-github.md`
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.WriteToStreamBenchmarks-report-github.md`

To avoid committing raw benchmark artifacts, the key baseline values are summarized here.

| Benchmark Slice | Method + Parameters (Before) | Before Mean |
|---|---|---:|
| Checkpoint generator write path | `WriteWithCheckpointGenerator` (`CommitsToWrite=1000`, `GeneratorType=Always`) | 2,908.6 ms |
| Checkpoint generator write path (comparison) | `WriteWithCheckpointGenerator` (`CommitsToWrite=1000`, `GeneratorType=InMemory`) | 1,541.0 ms |
| Global read (bucket-qualified) | `ReadFromBucketCheckpoint` (`CommitsPerBucket=1000`, `ExtraBuckets=3`) | 22.142 ms |
| Global read (all buckets) | `ReadFromAllBucketsCheckpoint` (`CommitsPerBucket=1000`, `ExtraBuckets=3`) | 67.011 ms |
| Per-stream full read | `ReadFromStream` (`CommitsToWrite=10000`) | 148.004 ms |
| Per-stream revision-window read | `ReadTailRevisionWindow` (`TotalCommitsInStream=10000`, `RevisionWindowSize=1000`) | 17.207 ms |
| Write path (sync) | `WriteToStream` (`CommitsToWrite=10000`) | 10,489.8 ms |
| Write path (async) | `WriteToStreamAsync` (`CommitsToWrite=10000`) | 10,705.0 ms |
| Global read (async) | `ReadFromEventStoreAsync` (`CommitsToWrite=10000`) | 120.443 ms |
| Snapshot overhead slice | `WriteToStream` (`CommitsToWrite=1000`, `DisableSnapshotSupport=False`, `PersistStreamHeadsOnBackgroundThread=True`) | 2,631.6 ms |
| Recycle-bin read slice | `ReadDeletedCommitsFromRecycleBinBucket` (`CommitsPerStream=1000`, `DeletedStreams=5`, `ActiveStreams=1`) | 108.028 ms |
| Duplicate conflict slice | `DuplicateCommitIdPath` (`Iterations=100`) | 683.60 ms |

## Explain Audit Workflow

Use the explain audit script to validate the index usage of the persistence engine query shapes against a local MongoDB container:

```powershell
.\scripts\explain-persistence-engine.ps1
```

The script seeds a scratch database, recreates the same indexes defined by the engine, and runs `explain("executionStats")` for the main `Find` and equivalent delete/update filter shapes in `MongoPersistenceEngine`.

Current findings from the issue [#73](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/73) follow-up:

- The changed query shapes for stream reads, snapshot reads, snapshot deletes, and bucket-scoped stream-head reads all use the intended indexes.
- Bucket checkpoint scans and duplicate-commit lookups also use the expected indexes.
- The legacy date-based bucket reads and all-buckets checkpoint scans remain less ideal query shapes and should be reviewed separately if they become performance-sensitive.
- Decision: do not add a new `CommitStamp`-oriented compound index for the obsolete `GetFrom(bucketId, DateTime)` and `GetFromTo(bucketId, DateTime, DateTime)` APIs. They are sync-only compatibility methods on an upstream obsolete contract, they are already documented for removal, and the preferred checkpoint-based APIs are the supported optimization target.

### After Snapshot Template

After implementing optimizations, run the same net10 baseline profile and fill this table using the same method/parameter rows selected from the "before" reports.

| Benchmark Slice | Before Mean | After Mean | Delta % |
|---|---:|---:|---:|
| Checkpoint generator write path (Always, 1000 commits) | 2,908.6 ms |  |  |
| Global read (bucket-qualified, 1000/3) | 22.142 ms |  |  |
| Global read (all buckets, 1000/3) | 67.011 ms |  |  |
| Per-stream full read (10000 commits) | 148.004 ms | 120.660 ms | -18.5% |
| Per-stream revision-window read (10000, window 1000) | 17.207 ms | 14.732 ms | -14.4% |
| Write path (sync, 10000 commits) | 10,489.8 ms |  |  |
| Write path (async, 10000 commits) | 10,705.0 ms |  |  |
| Global read (async, 10000 commits) | 120.443 ms |  |  |
| Snapshot overhead slice (1000, snapshots on, bg on) | 2,631.6 ms |  |  |
| Recycle-bin read slice (1000, deleted=5, active=1) | 108.028 ms |  |  |
| Duplicate conflict slice (commit id, iterations=100) | 683.60 ms |  |  |

Delta formula:

```text
Delta % = ((After Mean - Before Mean) / Before Mean) * 100
```

### Snapshot Retention Policy

Store benchmark evidence at two levels:

- Commit summary data to git:
	- Keep the curated before/after comparison tables in this document.
	- This is the canonical performance history for code review and PR discussion.
- Keep raw artifacts out of git:
	- Do not commit files under `BenchmarkDotNet.Artifacts/results/`.
	- Archive each run as a timestamped zip in local or CI artifact storage.

Recommended archive naming:

- `benchmark-baseline-net10-YYYYMMDD-HHMM.zip`
- `benchmark-after-<optimization-id>-net10-YYYYMMDD-HHMM.zip`

Minimum workflow for each optimization pass:

1. Run the baseline profile and archive raw artifacts.
2. Update the Before/After table in this document.
3. Run optimized code with the same profile and archive raw artifacts.
4. Fill After Mean and Delta % values in the same rows.

Automation script:

- `scripts/benchmark-snapshot.ps1`

Example commands:

```powershell
# Baseline snapshot (net10)
powershell -ExecutionPolicy Bypass -File .\scripts\benchmark-snapshot.ps1 -SnapshotType baseline -Framework net10.0 -Filter *

# After snapshot for optimization "checkpoint-sort-fix"
powershell -ExecutionPolicy Bypass -File .\scripts\benchmark-snapshot.ps1 -SnapshotType after -OptimizationId checkpoint-sort-fix -Framework net10.0 -Filter *

# Faster smoke snapshot (optional, less stable numbers)
powershell -ExecutionPolicy Bypass -File .\scripts\benchmark-snapshot.ps1 -SnapshotType baseline -Framework net10.0 -Filter * -ShortRun
```