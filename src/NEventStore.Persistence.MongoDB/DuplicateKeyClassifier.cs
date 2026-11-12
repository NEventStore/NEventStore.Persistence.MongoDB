using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace NEventStore.Persistence.MongoDB
{
    internal enum DuplicateKeyKind { None, Checkpoint, Commit, Other }

    internal static class DuplicateKeyClassifier
    {
        internal static DuplicateKeyKind Classify(MongoException exception)
        {
            BsonDocument? details = null;
            string message = exception.Message;
            bool duplicate;
            if (exception is MongoWriteException write)
            {
                duplicate = write.WriteError.Category == ServerErrorCategory.DuplicateKey;
                details = write.WriteError.Details;
                message = write.WriteError.Message;
            }
            else if (exception is MongoCommandException command)
            {
                duplicate = command.Code == 11000 || command.Code == 11001 || command.Code == 12582;
                details = command.Result;
            }
            else
            {
                // Legacy responses lack typed details. Only an actual duplicate-key prefix qualifies.
                duplicate = Regex.IsMatch(message, @"^E1100[01]\s+duplicate key\b", RegexOptions.CultureInvariant);
            }
            if (!duplicate) return DuplicateKeyKind.None;

            if (details != null && details.TryGetValue("keyPattern", out var pattern) && pattern.IsBsonDocument)
            {
                var keys = pattern.AsBsonDocument;
                if (keys.ElementCount == 1 && keys.Contains(MongoCommitFields.CheckpointNumber)) return DuplicateKeyKind.Checkpoint;
                if (keys.ElementCount == 3 && keys.Contains(MongoCommitFields.BucketId)
                    && keys.Contains(MongoCommitFields.StreamId) && keys.Contains(MongoCommitFields.CommitId)) return DuplicateKeyKind.Commit;
                return DuplicateKeyKind.Other;
            }

            // Older servers expose the index only in errmsg; match its full token, not payload substrings.
            var index = Regex.Match(message, @"\bindex:\s+(?<name>\S+)\s+dup key\s*:", RegexOptions.CultureInvariant);
            if (!index.Success) return DuplicateKeyKind.Other;
            var name = index.Groups["name"].Value;
            if (name == "_id_" || name.EndsWith(".$_id_", System.StringComparison.Ordinal)) return DuplicateKeyKind.Checkpoint;
            if (name == MongoCommitIndexes.CommitId || name.EndsWith(".$" + MongoCommitIndexes.CommitId, System.StringComparison.Ordinal)) return DuplicateKeyKind.Commit;
            return DuplicateKeyKind.Other;
        }
    }
}
