# Stream-head metadata updates

Commits are authoritative. Stream heads are derived metadata, and their writes can
complete in a different order from accepted commits when background persistence
is enabled. Metadata writes retain their configured write concern.

## Commit-side invariants (#89)

Within one stream lifetime, each successfully applied commit metadata update:

- Raises `HeadRevision` to the maximum of its current value and the commit revision.
- Increments `Unsnapshotted` by that commit's event count, even if a newer head
  revision is already present. Filtering out old revisions would lose increments.
- Preserves `SnapshotRevision`, initializing it to zero for a missing head.

These changes occur together in one MongoDB document update. An upsert initializes
all three counters. Duplicate commit retries fail before scheduling a second
metadata update. This does not make replaying a metadata increment idempotent or
guarantee delivery of failed/unacknowledged background metadata writes.

## Snapshot and lifecycle boundaries

Commit-side updates must preserve snapshot-side metadata. With a stable snapshot
and all commit metadata applied, `Unsnapshotted` equals `HeadRevision` minus
`SnapshotRevision`. A snapshot update must account for concurrent commits and
older snapshots atomically to retain this relationship; that separate work is
tracked in [#90](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/90).
In the existing snapshot path, its read/compute/set sequence can overwrite an
intervening increment, and an older snapshot can lower `SnapshotRevision`.
The commit-side `$max` change alone does not correct either snapshot-side race.

Snapshot resynchronization also needs coordination with delayed commit metadata:
if the head reaches 6 while revision 4's two-event metadata update is pending, a
snapshot at 4 computes `Unsnapshotted = 2`; the delayed increment then makes it 4.
Computing the counter from the current head in a snapshot pipeline alone does not
resolve that interleaving. Combined commit/snapshot changes must review it before
claiming the counter is exact with pending background writes.

After writes have drained and deletion removes the head, a recreated stream starts
with fresh counters. A delayed write from a deleted lifetime can still recreate or
modify that head; preventing cross-lifetime writes requires the lifecycle work in
[#96](https://github.com/NEventStore/NEventStore.Persistence.MongoDB/issues/96).

This fix changes no public API or stored schema and requires no data migration.
It prevents future revision regression from reordered commit metadata writes;
it does not backfill already regressed heads.
