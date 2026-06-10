# Performance Investigation

This document captures the current performance state of `NEventStore.Persistence.MongoDB` before any engine changes.

## Scope

- Inspect the MongoDB persistence engine hot paths.
- Validate the current benchmark suite.
- Record the first set of evidence-backed optimization targets.
- Defer runtime code changes until the benchmark harness can produce trustworthy baselines.

## Current Handoff (2026-06-10)

This is the current state after reviewing `docs/Performance-Investigation.md`, local code, benchmark artifacts, and GitHub issues tagged `performance`.

### What is done

- Benchmark harness is usable from the CLI and has sync/async read/write coverage.
- Issue [#73](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/73) is closed by decision:
	- Per-stream reads now sort by `StreamRevisionFrom`, not `CheckpointNumber`.
	- `Issue73ExplainPlans` asserts `GetFrom_Index` is used and no `SORT` stage is present for the stream range read.
	- `Changelog.md` has a #73 entry.
- Issue [#74](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/74) is closed by decision:
	- Attempted eager-deserialization optimizations were too complex for the negligible gain observed.
	- The current `doc.ToCommit(_serializer)` path remains intentionally simple.
- The explain audit for issue [#75](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/75) has been investigated:
	- All-buckets checkpoint reads are not a COLLSCAN regression.
	- MongoDB chooses `_id_`, not `GetFrom_Checkpoint_Index`.
	- A partial `_id` index is not viable because MongoDB rejects `partialFilterExpression` on `_id`.
- Issue [#76](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/76) is measured and on standby:
	- `InMemoryCheckpointGenerator` is materially faster for writes.
	- Do not change the default because the current default preserves no-hole behavior after concurrency exceptions.
	- Treat `InMemoryCheckpointGenerator` as an explicit opt-in tuning option only.
- Issue [#77](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/77) is investigated and on standby:
	- The successful commit path does re-materialize the inserted BSON document via `commitDoc.ToCommit(_serializer)`.
	- The obvious shortcut would return original `CommitAttempt` event messages instead of serializer round-tripped messages.
	- Do not change without a dedicated microbenchmark and return-contract tests.
- Issue [#78](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/78) is measured and on standby:
	- Background stream-head updates did not show a clear repeatable wall-clock win.
	- No engine change is justified from the current benchmark data.
- Current benchmark slices cover the known hotspots: stream reads, global reads, checkpoint generator choice, snapshot/background updates, duplicate conflicts, recycle-bin reads, sync writes, and async writes.

### Issue decision summary

| Issue | Decision | Follow-up trigger |
|---|---|---|
| [#73](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/73) | Closed by decision. Keep the stream-read sort fix and explain-plan evidence. | Revisit only if a future benchmark or explain audit contradicts the current index-plan result. |
| [#74](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/74) | Closed by decision. Keep eager `ToCommit` materialization simple. | Revisit only with a simple, clearly measurable read-path win. |
| [#75](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/75) | Standby. Not a rebuild bottleneck and not a COLLSCAN regression. | Revisit only for recycle-bin-heavy all-buckets polling workloads. |
| [#76](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/76) | Standby/opt-in. `InMemoryCheckpointGenerator` is faster but changes checkpoint-hole behavior. | Document or expose guidance for callers that explicitly accept holes for write throughput. |
| [#77](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/77) | Standby. Return-path shortcut is observable and needs tighter tests. | Build a microbenchmark for `commitDoc.ToCommit(_serializer)` and return materialization contract tests. |
| [#78](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/78) | Standby. Background stream-head update comparison is noisy and no clear win. | Revisit only if write throughput is the target and a stronger benchmark isolates this path. |

### Conflicts to resolve

- Canonical rebuild-focused benchmark snapshots now exist for `ReadFromStreamBenchmarks`, `StreamRevisionWindowBenchmarks`, and `SnapshotAssistedRebuildBenchmarks`.
- The `ReadFromStream(10000)` after mean is slower than the original before value on this machine (`189.337 ms` after vs `148.004 ms` before), despite the #73 explain-plan fix. Treat #73 as an index-plan correctness fix, not a proven wall-clock speedup from the current benchmark data.
- The tail-window benchmark was changed to seed directly through `IPersistStreams`, so compare its new values against future runs of the same benchmark shape, not against the older before table.
- Rebuild-read benchmarks no longer pin `InvocationCount=1`; BenchmarkDotNet now chooses invocation counts through its pilot phase to avoid sub-100 ms iteration warnings.

### Rebuild-focused direction

The current optimization target is read-heavy rebuild operations, not write throughput or global checkpoint polling.

For aggregate rebuilds, the hot path is:

1. Optional snapshot lookup via `GetSnapshot(bucketId, streamId, maxRevision)`.
2. Stream commit read via `GetFrom(bucketId, streamId, minRevision, maxRevision)`.
3. Full `ICommit` and event payload materialization through `doc.ToCommit(_serializer)`.

Current implications:

- #73 is the main completed rebuild optimization: per-stream reads now sort by `StreamRevisionFrom`, matching `GetFrom_Index` and avoiding the old checkpoint-sort plan.
- #74 is intentionally closed: eager `ToCommit` materialization remains simple because attempted optimizations were too complex for negligible gain.
- #75 is not a rebuild bottleneck. It affects all-buckets checkpoint polling, so it stays in standby.
- #76 is a write-path opt-in trade-off, not a default change: `InMemoryCheckpointGenerator` is faster but can leave checkpoint holes after concurrency exceptions.
- #77 is a write-path standby item. It needs a dedicated microbenchmark and return-contract tests before any engine change.
- #78 is a write-path standby item. Keep it behind rebuild-focused read work unless write throughput becomes the target again.

What matters next for rebuilds:

- Use the #73 explain audit as the primary evidence that per-stream reads now use `GetFrom_Index` without a blocking `SORT`.
- Use the canonical rebuild benchmark snapshots below as the current read-heavy evidence.
- Compare future rebuild changes against the archived `rebuild-readfromstream`, `rebuild-tailwindow`, and `rebuild-snapshot-assisted` snapshots.
- Prefer snapshot-assisted rebuilds for long streams when the caller has a recent snapshot; the benchmark shows the largest allocation reduction there.

### What's left

| Priority | GitHub issue | Status | Remaining work |
|---:|---|---|---|
| 1 | Rebuild benchmark evidence | Done for current pass | Canonical rebuild snapshots are archived for full stream, tail-window, and snapshot-assisted rebuild reads. |
| 2 | [#73](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/73) stream-read sort | Closed by decision | Keep the explain-plan evidence as the reason for closure. Do not claim a wall-clock benchmark win from the current data. |
| 3 | [#75](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/75) all-buckets checkpoint scan | Standby after investigation | Not a rebuild bottleneck. Resume only if all-buckets polling with a large recycle bin becomes a target workload. |
| 4 | [#76](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/76) checkpoint generator DB read | Measured, write-path opt-in | Stabilized benchmark confirms `InMemoryCheckpointGenerator` is faster, but it changes checkpoint-hole behavior. Do not change the default. |
| 5 | [#78](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/78) stream-head background updates | Measured, write-path standby | Stabilized snapshot-overhead slice is archived. No engine change yet because background on/off remains noisy and shows no clear win. |
| 6 | [#77](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/77) post-insert re-deserialization | Investigated, write-path standby | Do not change without a dedicated microbenchmark and return-contract tests. Avoid returning the original `CommitAttempt` events unless serializer round-trip semantics are proven unnecessary. |

### Next recommended pass

1. Rerun the rebuild snapshots with the stabilized benchmark job before comparing future read-heavy changes.
2. Use the archived rebuild snapshots as historical evidence for the #73 pass, not as the final stable baseline.
3. Keep engine code simple unless a future benchmark shows a clear, repeatable rebuild win.
4. Revisit #75/#76/#78/#77 only if the target workload changes away from rebuild reads.
5. For write-side work, treat #76 as an opt-in tuning option, #77 as contract-sensitive standby, and #78 as measured/standby.

## Current Benchmark Status

The benchmark harness is now runnable and selectable from a single entrypoint.

### Baseline Profile (Before Snapshot)

Use this baseline profile for all future optimization comparisons:

- Runtime: `.NET 10.0` only (`net10.0` benchmark binary).
- Connection: `NEventStore.MongoDB=mongodb://localhost:50002/NEventStore`.
- Benchmark job: class-defined default (`LaunchCount=3`, `WarmupCount=3`, `IterationCount=3`, automatic invocation count).
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
- Snapshot-assisted aggregate rebuild reads.
- Delete and recycle-bin read behavior.
- Duplicate-commit and duplicate-checkpoint retry paths.

## High-Value Performance Findings

These are the strongest candidates for meaningful improvement, ordered by read-side first (higher leverage), then write-side findings. Confidence levels reflect data from benchmarks and index analysis.

### 1. Stream read queries no longer require checkpoint sort

**Read-side critical, implemented on this branch.** The original finding was that per-stream reads filtered on `(BucketId, StreamId, StreamRevisionFrom, StreamRevisionTo)` but sorted by `CheckpointNumber`.

Original index/query mismatch:

```
INDEX: (BucketId, StreamId, StreamRevisionFrom, StreamRevisionTo)
QUERY FILTER: BucketId = X, StreamId = Y, revision range
QUERY SORT: CheckpointNumber ASC
```

Why this mattered:

- The index supports the filter but NOT the sort (CheckpointNumber is not in the index).
- MongoDB must perform an in-memory sort after filtering, which is expensive for large streams.
- Large aggregate reconstruction (many commits per stream) will hit this bottleneck directly.

Current state:

- Per-stream sync and async reads now sort by `StreamRevisionFrom`.
- The existing `GetFrom_Index` supports the filter and sort shape.
- `Issue73ExplainPlans.Commit_range_read_should_use_GetFrom_index_without_a_sort_stage` asserts index usage and no `SORT` stage.
- Remaining work is documentation/issue closeout plus a clean after benchmark rerun.

### 2. Read paths eagerly deserialize commits by design

**Closed by decision.** Every `GetFrom*` path ends in `doc.ToCommit(_serializer)`.

`ToCommit` currently does all of the following for every document:

- Deserializes the whole `MongoCommit` object from `BsonDocument`.
- Iterates every stored event.
- Deserializes each `EventMessage` payload.
- Materializes the event set into an array.

Why this was investigated:

- Global checkpoint scans pay full payload materialization cost even when the caller only needs iteration or metadata.
- This cost grows with event count and payload size.
- Allocation pressure will likely dominate long sequential reads (especially for large event counts per commit).

Decision:

- Attempted optimizations were too complex for the negligible gain observed.
- Keep the current eager `ToCommit` path because it is simple and preserves the existing `ICommit` materialization behavior.
- No further #74 optimization work is planned.

### 3. All-buckets checkpoint reads use inefficient filter

**Read-side secondary, workload-dependent.** The all-buckets checkpoint scan uses `BucketId != :rb` to exclude recycled streams.

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

Follow-up investigation from issue comments:

- The benchmark comparison is not apples-to-apples: `ReadFromAllBucketsCheckpoint` returns all active buckets, while `ReadFromBucketCheckpoint` returns only `Bucket.Default`.
- With `ExtraBuckets=3`, all-buckets returns 4x as many commits. The 67.011 ms vs 22.142 ms result is therefore not proof of a 3x per-commit regression.
- Rewriting `BucketId != :rb` as an `$or` split around `:rb` does not help. MongoDB still chooses `_id_`; forcing `GetFrom_Checkpoint_Index` adds `SORT + OR`.
- A partial compound index using `{ _id: 1, BucketId: 1 }` with `partialFilterExpression: { BucketId: { $ne: ':rb' } }` is also rejected because `$ne` is unsupported in partial indexes.
- A non-partial `{ _id: 1, BucketId: 1 }` index is legal and can reduce `totalDocsExamined` by filtering `:rb` from index keys before fetch, but it still scans the same checkpoint key range and adds write/storage cost.
- In a recycle-bin-heavy scratch shape, MongoDB may prefer `GetFrom_Checkpoint_Index` plus an explicit `SORT` when the active bucket set is tiny. That is a workload-specific trade-off, not a safe default optimization.

Current recommendation:

- Put #75 in standby based on the investigation.
- Do not change the engine for #75 based on current evidence.
- Resume only with a dedicated benchmark for recycle-bin-heavy all-buckets polling before considering `{ _id: 1, BucketId: 1 }`.
- Treat regular recycle-bin cleanup as the preferred operational mitigation.

### 4. Default checkpoint generation adds a database read per commit

**Write-side opt-in trade-off.** `MongoPersistenceEngine.Initialize()` defaults to `AlwaysQueryDbForNextValueCheckpointGenerator`.

That generator calls `GetLastValue()` on every `Next()` invocation, which means one extra database read for every commit before the insert even happens.

Why this matters:

- Write-heavy workloads will pay an extra round trip per commit.
- However, read-side deserialization cost typically dominates for mixed workloads.
- The cost scales directly with commit volume.

What needs benchmarking first:

- Completed: current default generator vs `InMemoryCheckpointGenerator` behavior is covered by `CheckpointGeneratorBenchmarks`.
- Do not change the default generator without a breaking-change decision, because acceptance tests cover the current no-hole default after concurrency exceptions.
- Use `InMemoryCheckpointGenerator` only as an explicit write-throughput tuning option for callers that accept checkpoint holes after concurrency conflicts.

### 5. Commit success path re-deserializes the inserted document

**Write-side standby.** After a successful insert, `Commit()` returns `commitDoc.ToCommit(_serializer)`.

That means the write path serializes the commit to BSON for insert, then immediately deserializes the same BSON back to `ICommit` to return it.

Why this matters:

- Pure post-insert CPU and allocation overhead.
- The cost is paid on every successful commit, but write insertion is typically not the loop bottleneck.
- It is independent of MongoDB round-trip latency.

Follow-up investigation:

- Sync and async commit paths both return `commitDoc.ToCommit(_serializer)` after successful insert.
- The obvious shortcut is constructing `Commit` directly from `CommitAttempt` plus the generated checkpoint, but that would return the caller's event messages rather than the serializer round-tripped event messages currently produced by `ToCommit`.
- Commit hooks receive the returned `ICommit`, so this is observable behavior, not only an internal allocation detail.
- A stabilized full `WriteToStreamBenchmarks` run was attempted for this slice, but the 10,000-commit case exceeded the command timeout and produced no usable archive.

Current recommendation:

- Keep #77 in standby.
- Do not change the runtime commit return path without a dedicated microbenchmark that isolates `commitDoc.ToCommit(_serializer)` and tests that lock down acceptable return materialization semantics.
- If write throughput becomes the target again, prefer a narrow benchmark that compares `commitDoc.ToCommit(_serializer)` against a candidate `CommitAttempt`-based materializer before touching `MongoPersistenceEngine.Commit`.

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

The next performance pass should focus on aggregate rebuild reads.

1. Compare future read-heavy changes against the stabilized rebuild snapshots.
2. If `SnapshotAssistedRebuild(10000, 1000)` remains noisy in future runs, isolate it and rerun before using its mean in a decision.
3. Keep #75 in standby, treat #76 as an opt-in write tuning option, and defer #78/#77 unless write throughput becomes the target again.

## Rebuild Benchmark Snapshot

These values are from stabilized class-defined BenchmarkDotNet jobs on 2026-06-10 using `LaunchCount=3`, `WarmupCount=3`, `IterationCount=3`, and automatic invocation counts selected by BenchmarkDotNet's pilot phase.

Archived raw outputs:

- `artifacts/benchmark-snapshots/benchmark-after-rebuild-readfromstream-stabilized-net10.0-20260610-1451.zip`
- `artifacts/benchmark-snapshots/benchmark-after-rebuild-tailwindow-stabilized-net10.0-20260610-1501.zip`
- `artifacts/benchmark-snapshots/benchmark-after-rebuild-snapshot-assisted-stabilized-net10.0-20260610-1515.zip`

These stabilized runs emitted no `MinIterationTime` warnings. Historical pinned-invocation archives from the #73 pass remain useful for traceability, but future read-heavy comparisons should use the stabilized snapshots above.

### Full Stream Read

| Benchmark | Parameters | Mean | Allocated |
|---|---|---:|---:|
| `ReadFromStream` | `CommitsToWrite=100` | 3.174 ms | 699.78 KB |
| `ReadFromStream` | `CommitsToWrite=1000` | 17.476 ms | 6,972.27 KB |
| `ReadFromStream` | `CommitsToWrite=10000` | 205.481 ms | 69,760.60 KB |

### Tail Revision Window

| Total commits | Window | Mean | Allocated |
|---:|---:|---:|---:|
| 1,000 | 10 | 2.648 ms | 91.90 KB |
| 1,000 | 100 | 3.800 ms | 713.21 KB |
| 1,000 | 1,000 | 30.759 ms | 6,957.40 KB |
| 10,000 | 10 | 12.932 ms | 91.92 KB |
| 10,000 | 100 | 16.359 ms | 713.17 KB |
| 10,000 | 1,000 | 31.403 ms | 6,971.67 KB |

The allocation shape is the useful signal here: allocations track the returned window size, while the fixed overhead of seeking a tail window grows with larger stream size.

### Snapshot-Assisted Rebuild

| Total commits | Commits after snapshot | Full rebuild | Snapshot-assisted | Full alloc | Snapshot alloc |
|---:|---:|---:|---:|---:|---:|
| 1,000 | 10 | 14.386 ms | 3.128 ms | 6,972.37 KB | 99.10 KB |
| 1,000 | 100 | 16.210 ms | 6.354 ms | 6,972.34 KB | 732.74 KB |
| 1,000 | 1,000 | 18.163 ms | 18.451 ms | 6,972.40 KB | 6,973.17 KB |
| 10,000 | 10 | 195.717 ms | 12.842 ms | 69,760.78 KB | 99.44 KB |
| 10,000 | 100 | 204.460 ms | 13.877 ms | 69,760.78 KB | 732.74 KB |
| 10,000 | 1,000 | 205.723 ms | 52.314 ms | 69,760.77 KB | 6,994.45 KB |

Snapshot-assisted rebuilds materially reduce work when the snapshot is near the tail. For 10,000-commit streams, allocations drop from about 68 MB for full rebuilds to about 0.1 MB, 0.7 MB, or 6.8 MB depending on the number of commits after the snapshot. The `10,000 / 1,000` snapshot-assisted timing had high variance in this run (`52.314 ms` mean, `43.982 ms` stddev), but its allocation reduction is still clear.

## Write-Side Snapshot-Overhead Snapshot

Issue [#78](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/78) was rechecked on 2026-06-10 with `SnapshotOverheadBenchmarks` after removing the pinned `InvocationCount=1` from the benchmark job. BenchmarkDotNet now selects invocation counts through its pilot phase for this slice too.

Archived raw output:

- `artifacts/benchmark-snapshots/benchmark-after-write-snapshot-overhead-stabilized-net10.0-20260610-1712.zip`

| Commits | Snapshot support disabled | Background stream-head update | Mean | StdDev | Allocated |
|---:|---|---|---:|---:|---:|
| 100 | false | false | 206.2 ms | 47.78 ms | 22.69 MB |
| 100 | false | true | 220.3 ms | 140.86 ms | 22.70 MB |
| 100 | true | false | 149.2 ms | 16.86 ms | 28.16 MB |
| 100 | true | true | 140.1 ms | 4.12 ms | 21.08 MB |
| 1,000 | false | false | 1,724.0 ms | 362.08 ms | 110.82 MB |
| 1,000 | false | true | 1,676.5 ms | 477.40 ms | 110.96 MB |
| 1,000 | true | false | 1,623.6 ms | 366.49 ms | 94.77 MB |
| 1,000 | true | true | 1,954.2 ms | 419.49 ms | 94.74 MB |

Current decision:

- Background stream-head updates do not show a clear repeatable wall-clock win.
- With snapshot support enabled, 1,000-commit allocation remains about 111 MB regardless of background mode.
- Disabling snapshot support lowers 1,000-commit allocation to about 95 MB, but that changes behavior and only applies to callers that do not need snapshots.
- Keep #78 in standby. Do not replace the current simple per-commit background update path without a stronger write-throughput benchmark signal.

## Write-Side Checkpoint-Generator Snapshot

Issue [#76](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/76) was rechecked on 2026-06-10 with `CheckpointGeneratorBenchmarks` after removing the pinned `InvocationCount=1` from the benchmark job. BenchmarkDotNet now selects invocation counts through its pilot phase for this slice too.

Archived raw output:

- `artifacts/benchmark-snapshots/benchmark-after-write-checkpoint-generator-stabilized-net10.0-20260610-1723.zip`

| Commits | Generator | Mean | StdDev | Allocated |
|---:|---|---:|---:|---:|
| 100 | Always | 244.3 ms | 14.29 ms | 16.88 MB |
| 100 | InMemory | 147.7 ms | 6.87 ms | 28.24 MB |
| 1,000 | Always | 2,506.2 ms | 238.76 ms | 110.96 MB |
| 1,000 | InMemory | 1,322.9 ms | 274.52 ms | 95.54 MB |

Current decision:

- `InMemoryCheckpointGenerator` is materially faster for write throughput in this benchmark.
- The default `AlwaysQueryDbForNextValueCheckpointGenerator` should not be changed in this pass because it preserves the current no-hole default after concurrency exceptions.
- `InMemoryCheckpointGenerator` remains the safe performance path only as explicit caller configuration when checkpoint holes after concurrency conflicts are acceptable.
- Keep #76 as measured/opt-in rather than a runtime engine change.

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
- `NEventStore.Persistence.MongoDB.Benchmark.Benchmarks.SnapshotAssistedRebuildBenchmarks-report-github.md`
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

Current caution: the #73 full-stream after value is cleanly captured with the stabilized benchmark job, but it does not show a wall-clock win on this machine. Use the explain-plan audit as the primary #73 correctness evidence. The tail-window after value uses the revised persistence-level benchmark shape, so it is not comparable to the older before value.

| Benchmark Slice | Before Mean | After Mean | Delta % |
|---|---:|---:|---:|
| Checkpoint generator write path (Always, 1000 commits) | 2,908.6 ms |  |  |
| Global read (bucket-qualified, 1000/3) | 22.142 ms |  |  |
| Global read (all buckets, 1000/3) | 67.011 ms |  |  |
| Per-stream full read (10000 commits) | 148.004 ms | 205.481 ms | +38.8% |
| Per-stream revision-window read (10000, window 1000) | 17.207 ms | 31.403 ms | Not comparable; benchmark shape changed |
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
