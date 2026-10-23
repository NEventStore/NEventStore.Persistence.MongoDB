# Recycle-bin identity and deletion

`DeleteStream` and `DeleteStreamAsync` recycle commits in the existing `Commits`
collection. They do not move documents to another collection or rebuild indexes.

## Stored and public identities

Each deletion attempt assigns one random `Guid.NewGuid().ToString("N")` value to
the stored `StreamId` of its matching commits, changes `BucketId` to `:rb`, and
preserves the source identity in `OriginalBucketId` and `OriginalStreamId`.
Different buckets and recreated streams can therefore use the same original
stream ID without sharing recycle-bin unique-index keys. The existing unique
indexes still reject a conflicting generated key rather than overwrite data.

Commit checkpoint (`_id`), commit ID, revisions, sequence, headers and events are
unchanged. The generated value is an internal deletion-attempt identifier, not a
predictable sequence, a timestamp, or a durable identifier for an entire stream
lifetime.

The adapter returns the original `StreamId` for recycled commits. Their public
`BucketId` remains `:rb`; `OriginalBucketId` is retained in BSON, not exposed as
the public bucket. `GetFrom(":rb", originalStreamId, minRevision, maxRevision)`
and its async equivalent search both new metadata and legacy stored IDs. This
lookup combines matching histories across original buckets and deletion attempts;
it does not select one historical stream incarnation.

## Existing data and rollout

Existing recycled documents without `OriginalStreamId` are read using their
stored `StreamId`. No backfill is required. Their original bucket was already
discarded by the old deletion implementation and cannot be inferred as `default`.
Live documents ignore optional recycled-identity metadata.

Old adapter versions tolerate the extra BSON fields, but expose the generated
stored ID when reading new recycled documents. Their original-stream lookup
does not find those new documents. Raw BSON consumers must also account for the
new identity fields. Upgrade recycle-bin readers before creating new-format
deleted data; this is not unrestricted mixed-version compatibility.

All existing indexes and checkpoint-based cleanup remain unchanged.
`EmptyRecycleBin` and its async equivalent remove recycled commits older than the
greatest stored checkpoint, retaining that checkpoint if it belongs to a recycled
commit. Original-ID lookups now use an additional predicate; their performance
has not been benchmarked.

## Failure and concurrency limits

Recycling is performed before deleting the stream head and snapshots. With the
default acknowledged commit write concern, a rejected recycle update propagates
an error before metadata cleanup begins. `UpdateMany` is not atomic across
documents: earlier commits may already be recycled when a later update fails.
An explicit retry matches the remaining live commits only and assigns a new
generated ID. Already recycled documents keep their stored identity; public
readers combine the groups using `OriginalStreamId`.

There is no automatic recovery job or persisted deletion-operation token. Tests
cover retry from a seeded partial state, not process termination/restart or
concurrent deletion and recreation. A delayed retry can match newly recreated
commits because the API identifies only a bucket and stream, not an incarnation.

For controlled deletion, stop commits, snapshots and recreation for that stream,
drain outstanding/background metadata writes, and keep that boundary in place
through failures and retries. Readers requiring all-or-nothing visibility must
also be excluded during this window. Reconcile remaining live commits, stream
heads and snapshots before allowing recreation. Default head/snapshot writes are
unacknowledged, so successful return alone is not a verified cleanup barrier;
use acknowledged metadata settings and verify the resulting state when this
guarantee is required. Overriding commit writes to be unacknowledged also removes
the acknowledged-recycling guarantee.

This change fixes recycle-key collisions and cleanup ordering. It does not add
transactions, writer fencing, atomic stream lifecycle operations, or stronger
write durability. Those require a separate lifecycle/API design.

The broader lifecycle work is tracked in [#96](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/96).
Existing asynchronous cancellation behavior is tracked separately in
[#86](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/86);
this identity change does not fix or strengthen that behavior.
