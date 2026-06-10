Be concise and token-efficient. Give direct answers, minimal examples, and no extra background.
No sycophantic openers or closing fluff. No emojis or em-dashes.

These rules apply to every task in this project unless explicitly overridden.
Bias: caution over speed on non-trivial work. Use judgment on trivial tasks.

## Rule 1 — Think Before Coding
State assumptions explicitly. If uncertain, ask rather than guess.
Present multiple interpretations when ambiguity exists.
Push back when a simpler approach exists.
Stop when confused. Name what's unclear.

## Rule 2 — Simplicity First
Minimum code that solves the problem. Nothing speculative.
No features beyond what was asked. No abstractions for single-use code.
Test: would a senior engineer say this is overcomplicated? If yes, simplify.

## Rule 3 — Surgical Changes
Touch only what you must. Clean up only your own mess.
Don't "improve" adjacent code, comments, or formatting.
Don't refactor what isn't broken. Match existing style.

## Rule 4 — Goal-Driven Execution
Define success criteria. Loop until verified.
Don't follow steps. Define success and iterate.
Strong success criteria let you loop independently.

## Rule 5 — Use the model only for judgment calls
Use me for: classification, drafting, summarization, extraction.
Do NOT use me for: routing, retries, deterministic transforms.
If code can answer, code answers.

## Rule 6 — Token budgets are not advisory
Per-task: 4,000 tokens. Per-session: 30,000 tokens.
If approaching budget, summarize and start fresh.
Surface the breach. Do not silently overrun.

## Rule 7 — Surface conflicts, don't average them
If two patterns contradict, pick one (more recent / more tested).
Explain why. Flag the other for cleanup.
Don't blend conflicting patterns.

## Rule 8 — Read before you write
Before adding code, read exports, immediate callers, shared utilities.
"Looks orthogonal" is dangerous. If unsure why code is structured a way, ask.

## Rule 9 — Tests verify intent, not just behavior
Tests must encode WHY behavior matters, not just WHAT it does.
A test that can't fail when business logic changes is wrong.

## Rule 10 — Checkpoint after every significant step
Summarize what was done, what's verified, what's left.
Don't continue from a state you can't describe back.
If you lose track, stop and restate.

## Rule 11 — Match the codebase's conventions, even if you disagree
Conformance > taste inside the codebase.
If you genuinely think a convention is harmful, surface it. Don't fork silently.

## Rule 12 — Fail loud
"Completed" is wrong if anything was skipped silently.
"Tests pass" is wrong if any were skipped.
Default to surfacing uncertainty, not hiding it.

# NEventStore.Persistence.MongoDB Project Guidelines

## Scope

NEventStore.Persistence.MongoDB is a **MongoDB persistence adapter** that implements `IPersistStreams` for the NEventStore event sourcing library. It enables storing event commits, snapshots, and stream metadata in MongoDB instead of relational databases.

- Treat the contract defined by `IPersistStreams` as a behavioral specification, not implementation detail
- Preserve commit ordering, optimistic concurrency, duplicate commit detection, and snapshot semantics
- Maintain thread-safe `MongoPersistenceEngine` behavior; persistence instances are shared across threads
- Support both synchronous and asynchronous code paths in parallel (see Conventions below)

## Architecture

### Core Components
- **`MongoPersistenceEngine` (sync) / `MongoPersistenceEngine.Async` (async)** — Main persistence logic managing three collections: `Commits`, `Streams`, `Snapshots`
- **`MongoPersistenceFactory`** — Creates configured `MongoPersistenceEngine` instances with dependency injection
- **`MongoPersistenceWireup`** — Fluent configuration API in the `NEventStore` namespace (not nested) for transparent integration
- **`MongoPersistenceOptions`** — Encapsulates MongoClient, WriteConcern, database name, and collection naming
- **`MongoCommit` / `MongoFields`** — BSON class map and field constant definitions

### Collections & Schema
Three collections store the event stream state:
1. **`Commits`** — Event commits with headers, events, and checkpoint numbers (uses `Acknowledged` write concern)
2. **`Streams`** — Stream metadata (ETag, RecycleBin marker, uses `Unacknowledged` write concern for performance)
3. **`Snapshots`** — Snapshots per stream version (uses `Unacknowledged` write concern)

## Build And Test

### Local Development
```powershell
# Start MongoDB (required for tests; see docker/ folder for Compose scripts)
.\docker\DockerComposeUp.ps1

# Interactive build + optional tests
.\build.ps1

# Direct commands
dotnet restore ./src/NEventStore.Persistence.MongoDB.Core.sln --verbosity m
dotnet build ./src/NEventStore.Persistence.MongoDB.Core.sln -c Release --no-restore
dotnet test ./src/NEventStore.Persistence.MongoDB.Core.sln -c Release --no-build
```

### CI/CD & Versioning
- **GitHub Actions** build on Windows machines and run tests on MongoDB on linux.
- **GitVersion** auto-patches assembly info from Git tags (do NOT manually edit version metadata)
- **GitFlow workflow**: `release/*` and `hotfix/*` branches lock version increments
- **NuGet packaging**: `nuget pack ./src/.nuget/NEventStore.Persistence.MongoDB.nuspec` outputs symbols package

### Test Environment
- Set env var: `NEventStore.MongoDB=mongodb://localhost:27017/NEventStore`
- NUnit is active test runner (configured via `DefineConstants=NUNIT` in `.csproj`)
- Acceptance tests inherit from `AcceptanceTestMongoPersistenceFactory` which appends a GUID to database names to prevent parallel test conflicts
- Use filter `-Trait:"Explicit"` in Visual Studio test explorer to exclude long-running explicit tests

## Conventions

### Sync + Async Split (Critical Pattern)
- **Every feature must exist in both `MongoPersistenceEngine.cs` (sync) and `MongoPersistenceEngine.Async.cs` (async)**
- Sync methods use blocking LINQ-to-MongoDB; async methods use `IAsyncCursor` and `Task` patterns
- Copy the sync version, convert `IMongoCollection<T>` operations to async equivalents, replace blocking loops with async iteration
- Cancellation tokens are threaded through async paths; sync paths ignore them
- Behavioral parity is required (same logic, same error handling)

### GUID Serialization (Critical Gotcha)
MongoDB driver v2.30.0 uses `CSharpLegacy` (legacy byte ordering); v3.0.0+ default to `Standard` (compatible across drivers).

**For backward compatibility, always use `CSharpLegacy`:**
```csharp
// Register globally once at startup
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));

// Or configure per field
BsonClassMap.RegisterClassMap<MongoCommit>(cm =>
{
    cm.AutoMap();
    cm.GetMemberMap(c => c.CommitId).SetSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
});
```

**For Dictionary/Object serialization:**
```csharp
BsonSerializer.RegisterSerializer(new ObjectSerializer(
    BsonSerializer.LookupDiscriminatorConvention(typeof(object)), 
    GuidRepresentation.CSharpLegacy, 
    ObjectSerializer.AllAllowedTypes));
```

### Write Concerns
- **Commits**: `Acknowledged` — Safety critical (cannot lose events)
- **Streams & Snapshots**: `Unacknowledged` — Performance optimization (metadata can be reconstructed)
- Do not change without understanding the trade-off

### Field Constants (Not Magic Strings)
Always use constants from `MongoCommitFields` or `MongoFields`:
```csharp
// GOOD
update = Builders<MongoCommit>.Update.Set(x => x.CheckpointNumber, checkpoint);

// ACCEPTABLE (MongoCommitFields.CheckpointNumber = "CheckpointNumber")
update = Builders<MongoCommit>.Update.Set(MongoCommitFields.CheckpointNumber, checkpoint);

// AVOID
update = Builders<MongoCommit>.Update.Set("CheckpointNumber", checkpoint); // Magic string
```

### Wireup Pattern
`MongoPersistenceWireup` is placed in the `NEventStore` namespace (not nested) for seamless extension method discovery:
```csharp
namespace NEventStore;

public static class MongoPersistenceWireup
{
    public static MongoPersistenceOptions UseMongoPersistence(this Wireup wireup, ...)
    {
        // ...
    }
}
```

Users then configure via fluent API:
```csharp
Wireup.Init()
    .UseMongoPersistence(mongoClient, options)
    .LogToOutputWindow()
    .Build();
```

### RecycleBin Pattern (Soft Deletes)
- Deleted streams are marked with `MongoSystemBuckets.RecycleBin = ":rb"` 
- Not automatically cleaned up since v12.0 (caller responsibility)
- Test isolation uses unique database names per run (see `AcceptanceTestMongoPersistenceFactory`)

### Cancellation & Task Semantics
- Async methods accept `CancellationToken` parameters and honor them
- Do not introduce breaking cancellation behavior changes unless explicitly required
- Sync code does not need cancellation support

## Dependencies

### NEventStore Core
The `dependencies/NEventStore/` folder is a Git submodule containing core interfaces:
- `IPersistStreams`, `IEventStream`, `ICommit`, `ISnapshot`
- `PersistenceWireup`, `IPipelineHook`, `IPipelineHookAsync`
- Update with: `git submodule update --init --recursive`

### External
- **MongoDB.Driver** (latest stable; currently 3.x with `Standard` GUID handling)
- **NUnit** 4.6.0, **MSTest** for testing
- **.NET Framework 4.7.2, .NET Standard 2.1, .NET 6.0+**

## Known Issues & Migration Notes

### v11.0.0+: MongoDB Driver 3.0 Breaking Change
MongoDB.Driver 3.0 defaults GUIDs to `Standard` representation (incompatible with older data). Existing deployments must explicitly register `CSharpLegacy` serializer at startup or migrate data.

### Test Isolation & Parallel Execution
`AcceptanceTestMongoPersistenceFactory` appends Guid to database names to prevent conflicts when tests run in parallel. Do not hardcode database names in tests.

### Removed RecycleBin Auto-Cleanup (v12.0+)
Previously, deleted streams were auto-removed. Now they're marked with RecycleBin flag. Call `PurgeRecycleBinAsync()` or `PurgeRecycleBin()` if needed.

## Documentation References
- See [README.md](README.md) for local setup and build instructions
- See [Changelog.md](Changelog.md) for versioned behavior changes before altering compatibility-sensitive code
- See [dependencies/NEventStore/Readme.md](dependencies/NEventStore/Readme.md) for core NEventStore architecture
- See [docs/](dependencies/NEventStore/docs/) in the NEventStore submodule for testing strategy, performance notes, and benchmarks

## Supplementary Task-Specific Instructions

Before making changes, check for scoped instructions or skills in the parent NEventStore project that may override conventions above:

- **Scoped instructions**: Check [`.github/instructions/`](dependencies/NEventStore/.github/instructions/) for files matching the area you are changing (applyTo glob patterns)
- **Reusable skills**: Check [`.agents/skills/`](dependencies/NEventStore/.agents/skills/) for step-by-step workflows for recurring tasks
