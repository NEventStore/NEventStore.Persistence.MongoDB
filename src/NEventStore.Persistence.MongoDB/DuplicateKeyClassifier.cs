using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace NEventStore.Persistence.MongoDB
{
    // None means this is not a recognized duplicate-key error; Other means it is a
    // duplicate, but not one of the two index identities that need special handling.
    internal enum DuplicateKeyKind { None, Checkpoint, Commit, Other }

    // Shared by sync and async commit error handling, not the successful write path.
    // Checkpoint collisions can be retried; commit collisions need duplicate-commit
    // handling. Do not infer either from field names appearing in a document's values.
    internal static class DuplicateKeyClassifier
    {
        internal static DuplicateKeyKind Classify(MongoException exception)
        {
            BsonDocument? details = null;
            string message = exception.Message;
            bool duplicate;
            // Prefer the driver's category or server code over text. A typed non-duplicate
            // error must not become a duplicate merely because its message mentions one.
            if (exception is MongoWriteException write)
            {
                // Write-concern-only failures have no WriteError. Preserve them for
                // normal storage-error translation, even if their text resembles a duplicate.
                if (write.WriteError == null) return DuplicateKeyKind.None;
                duplicate = write.WriteError.Category == ServerErrorCategory.DuplicateKey;
                details = write.WriteError.Details;
                message = write.WriteError.Message;
            }
            else if (exception is MongoCommandException command)
            {
                // Include historical duplicate-key codes returned by command responses.
                duplicate = command.Code == 11000 || command.Code == 11001 || command.Code == 12582;
                details = command.Result;
            }
            else
            {
                // Without typed metadata, accept only the anchored E11000/E11001 duplicate-key prefix.
                duplicate = Regex.IsMatch(message, @"^E1100[01]\s+duplicate key\b", RegexOptions.CultureInvariant);
            }
            if (!duplicate) return DuplicateKeyKind.None;

            // An available key pattern is authoritative, even when the message names a
            // different index. Match the complete field set, not a subset of a compound
            // index. Unknown patterns remain Other rather than falling back to text.
            if (details != null && details.TryGetValue("keyPattern", out var pattern) && pattern.IsBsonDocument)
            {
                var keys = pattern.AsBsonDocument;
                if (keys.ElementCount == 1 && keys.Contains(MongoCommitFields.CheckpointNumber)) return DuplicateKeyKind.Checkpoint;
                if (keys.ElementCount == 3 && keys.Contains(MongoCommitFields.BucketId)
                    && keys.Contains(MongoCommitFields.StreamId) && keys.Contains(MongoCommitFields.CommitId)) return DuplicateKeyKind.Commit;
                return DuplicateKeyKind.Other;
            }

            // Without a usable key pattern, older responses identify the index in errmsg.
            // Stop at the first value delimiter: a malformed or unknown header must not
            // let an index-like clause inside a duplicate value select a retry strategy.
            var payload = Regex.Match(message, @"\bdup key\s*:", RegexOptions.CultureInvariant);
            if (!payload.Success) return DuplicateKeyKind.Other;
            var header = message.Substring(0, payload.Index + payload.Length);
            // Extract the complete index token from that header. Unrecognized text stays Other.
            var index = Regex.Match(header, @"\bindex:\s+(?<name>\S+)\s+dup key\s*:", RegexOptions.CultureInvariant);
            if (!index.Success) return DuplicateKeyKind.Other;
            var name = index.Groups["name"].Value;
            // CheckpointNumber is stored as MongoDB's _id. Accept the bare index name
            // and the historical namespace.$index form, using ordinal exact comparisons.
            // This excludes lookalike names such as _id_custom or CommitId_extra.
            if (name == "_id_" || name.EndsWith(".$_id_", System.StringComparison.Ordinal)) return DuplicateKeyKind.Checkpoint;
            if (name == MongoCommitIndexes.CommitId || name.EndsWith(".$" + MongoCommitIndexes.CommitId, System.StringComparison.Ordinal)) return DuplicateKeyKind.Commit;
            return DuplicateKeyKind.Other;
        }
    }
}
