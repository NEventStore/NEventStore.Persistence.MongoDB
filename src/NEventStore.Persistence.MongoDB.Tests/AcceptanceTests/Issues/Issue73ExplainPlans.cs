using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;

#if MSTEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    internal sealed class ExplainPlanResult
    {
        public HashSet<string> Stages { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Indexes { get; } = new(StringComparer.Ordinal);

        public int TotalKeysExamined { get; set; }

        public int TotalDocsExamined { get; set; }
    }

#if MSTEST
    [TestClass]
#endif
    public class Issue_73_explain_should_use_expected_indexes : SpecificationBase
    {
        private MongoPersistenceEngine? _engine;
        private IMongoDatabase? _database;
        private IMongoCollection<BsonDocument>? _commits;
        private IMongoCollection<BsonDocument>? _streamHeads;
        private IMongoCollection<BsonDocument>? _snapshots;
        private Dictionary<string, ExplainPlanResult>? _plans;

        protected override void Context()
        {
            var options = new MongoPersistenceOptions
            {
                PersistStreamHeadsOnBackgroundThread = false,
            };

            var builder = new MongoUrlBuilder(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
            {
                DatabaseName = $"issue73-explain-{Guid.NewGuid():N}"
            };

            _database = options.ConnectToDatabase(builder.ToString());
            _engine = new MongoPersistenceEngine(_database, new DocumentObjectSerializer(), options);
            _engine.Initialize();

            _commits = _database.GetCollection<BsonDocument>("Commits");
            _streamHeads = _database.GetCollection<BsonDocument>("Streams");
            _snapshots = _database.GetCollection<BsonDocument>("Snapshots");

            SeedDocuments();
        }

        protected override void Because()
        {
            _plans = new Dictionary<string, ExplainPlanResult>(StringComparer.Ordinal)
            {
                ["commitRangeRead"] = ExplainFind(
                    _commits!,
                    new BsonDocument
                    {
                        [MongoCommitFields.BucketId] = "default",
                        [MongoCommitFields.StreamId] = "stream-1",
                        [MongoCommitFields.StreamRevisionTo] = new BsonDocument("$gte", 3),
                        [MongoCommitFields.StreamRevisionFrom] = new BsonDocument("$lte", 6)
                    },
                    new BsonDocument(MongoCommitFields.StreamRevisionFrom, 1)),
                ["bucketCheckpointRead"] = ExplainFind(
                    _commits!,
                    new BsonDocument
                    {
                        [MongoCommitFields.BucketId] = "default",
                        [MongoCommitFields.CheckpointNumber] = new BsonDocument("$gt", 3L)
                    },
                    new BsonDocument(MongoCommitFields.CheckpointNumber, 1)),
                ["duplicateCommitLookup"] = ExplainFind(
                    _commits!,
                    new BsonDocument
                    {
                        [MongoCommitFields.BucketId] = "default",
                        [MongoCommitFields.StreamId] = "stream-1",
                        [MongoCommitFields.CommitId] = "commit-1"
                    }),
                ["streamsToSnapshot"] = ExplainFind(
                    _streamHeads!,
                    new BsonDocument
                    {
                        [MongoStreamHeadFields.FullQualifiedBucketId] = "default",
                        [MongoStreamHeadFields.Unsnapshotted] = new BsonDocument("$gte", 0)
                    },
                    new BsonDocument(MongoStreamHeadFields.Unsnapshotted, -1)),
                ["getSnapshot"] = ExplainFind(
                    _snapshots!,
                    new BsonDocument
                    {
                        [MongoSnapshotFields.FullQualifiedBucketId] = "default",
                        [MongoSnapshotFields.FullQualifiedStreamId] = "stream-1",
                        [MongoSnapshotFields.FullQualifiedStreamRevision] = new BsonDocument("$lte", 6)
                    },
                    new BsonDocument(MongoSnapshotFields.FullQualifiedStreamRevision, -1),
                    1),
                ["deleteStreamSnapshotsFilter"] = ExplainFind(
                    _snapshots!,
                    new BsonDocument
                    {
                        [MongoSnapshotFields.FullQualifiedBucketId] = "default",
                        [MongoSnapshotFields.FullQualifiedStreamId] = "stream-1"
                    }),
                ["deleteStreamHeadById"] = ExplainFind(
                    _streamHeads!,
                    new BsonDocument(MongoStreamHeadFields.Id, new BsonDocument
                    {
                        [MongoStreamHeadFields.BucketId] = "default",
                        [MongoStreamHeadFields.StreamId] = "stream-1"
                    })),
                ["purgeBucketSnapshotsFilter"] = ExplainFind(
                    _snapshots!,
                    new BsonDocument(MongoSnapshotFields.FullQualifiedBucketId, "default")),
                ["purgeBucketStreamHeadsFilter"] = ExplainFind(
                    _streamHeads!,
                    new BsonDocument(MongoStreamHeadFields.FullQualifiedBucketId, "default")),
            };
        }

        protected override void Cleanup()
        {
            if (_database != null)
            {
                _database.Client.DropDatabase(_database.DatabaseNamespace.DatabaseName);
            }

            _engine?.Dispose();
        }

        [Fact]
        public void Commit_range_read_should_use_GetFrom_index_without_a_sort_stage()
        {
            _plans!["commitRangeRead"].Indexes.Should().Contain(MongoCommitIndexes.GetFrom);
            _plans["commitRangeRead"].Stages.Should().NotContain("SORT");
        }

        [Fact]
        public void Bucket_checkpoint_read_should_use_GetFromCheckpoint_index()
        {
            _plans!["bucketCheckpointRead"].Indexes.Should().Contain(MongoCommitIndexes.GetFromCheckpoint);
        }

        [Fact]
        public void Duplicate_commit_lookup_should_use_CommitId_index()
        {
            _plans!["duplicateCommitLookup"].Indexes.Should().Contain(MongoCommitIndexes.CommitId);
        }

        [Fact]
        public void Streams_to_snapshot_should_use_bucket_unsnapshotted_index()
        {
            _plans!["streamsToSnapshot"].Indexes.Should().Contain(MongoStreamIndexes.BucketUnsnapshotted);
            _plans["streamsToSnapshot"].Stages.Should().NotContain("SORT");
        }

        [Fact]
        public void Snapshot_lookup_should_use_bucket_stream_revision_index()
        {
            _plans!["getSnapshot"].Indexes.Should().Contain(MongoSnapshotIndexes.BucketStreamRevision);
            _plans["getSnapshot"].TotalKeysExamined.Should().BeGreaterThan(0);
        }

        [Fact]
        public void Snapshot_delete_filter_should_use_bucket_stream_revision_index()
        {
            _plans!["deleteStreamSnapshotsFilter"].Indexes.Should().Contain(MongoSnapshotIndexes.BucketStreamRevision);
        }

        [Fact]
        public void Stream_head_delete_by_id_should_use_the_builtin_id_index()
        {
            _plans!["deleteStreamHeadById"].Indexes.Should().Contain("_id_");
        }

        [Fact]
        public void Bucket_purge_filters_should_use_their_prefix_indexes()
        {
            _plans!["purgeBucketSnapshotsFilter"].Indexes.Should().Contain(MongoSnapshotIndexes.BucketStreamRevision);
            _plans["purgeBucketStreamHeadsFilter"].Indexes.Should().Contain(MongoStreamIndexes.BucketUnsnapshotted);
        }

        private void SeedDocuments()
        {
            for (int i = 0; i < 10; i++)
            {
                string bucketId = i < 8 ? "default" : "other";
                string streamId = i < 5 ? "stream-1" : $"stream-{i}";
                int baseRevision = (i * 2) + 1;

                _commits!.InsertOne(new BsonDocument
                {
                    [MongoCommitFields.CheckpointNumber] = i + 1,
                    [MongoCommitFields.BucketId] = bucketId,
                    [MongoCommitFields.StreamId] = streamId,
                    [MongoCommitFields.StreamRevisionFrom] = baseRevision,
                    [MongoCommitFields.StreamRevisionTo] = baseRevision + 1,
                    [MongoCommitFields.CommitSequence] = i + 1,
                    [MongoCommitFields.CommitId] = $"commit-{i + 1}",
                    [MongoCommitFields.CommitStamp] = DateTime.UtcNow.AddSeconds(i),
                    [MongoCommitFields.Headers] = new BsonDocument(),
                    [MongoCommitFields.Events] = new BsonArray(),
                });
            }

            _streamHeads!.InsertMany(new[]
            {
                new BsonDocument
                {
                    [MongoStreamHeadFields.Id] = new BsonDocument
                    {
                        [MongoStreamHeadFields.BucketId] = "default",
                        [MongoStreamHeadFields.StreamId] = "stream-1"
                    },
                    [MongoStreamHeadFields.HeadRevision] = 8,
                    [MongoStreamHeadFields.SnapshotRevision] = 2,
                    [MongoStreamHeadFields.Unsnapshotted] = 6,
                },
                new BsonDocument
                {
                    [MongoStreamHeadFields.Id] = new BsonDocument
                    {
                        [MongoStreamHeadFields.BucketId] = "default",
                        [MongoStreamHeadFields.StreamId] = "stream-2"
                    },
                    [MongoStreamHeadFields.HeadRevision] = 4,
                    [MongoStreamHeadFields.SnapshotRevision] = 0,
                    [MongoStreamHeadFields.Unsnapshotted] = 4,
                },
                new BsonDocument
                {
                    [MongoStreamHeadFields.Id] = new BsonDocument
                    {
                        [MongoStreamHeadFields.BucketId] = "other",
                        [MongoStreamHeadFields.StreamId] = "stream-1"
                    },
                    [MongoStreamHeadFields.HeadRevision] = 9,
                    [MongoStreamHeadFields.SnapshotRevision] = 0,
                    [MongoStreamHeadFields.Unsnapshotted] = 9,
                },
            });

            _snapshots!.InsertMany(new[]
            {
                CreateSnapshotDocument("default", "stream-1", 1, "s1-r1"),
                CreateSnapshotDocument("default", "stream-1", 3, "s1-r3"),
                CreateSnapshotDocument("default", "stream-1", 5, "s1-r5"),
                CreateSnapshotDocument("default", "stream-2", 2, "s2-r2"),
                CreateSnapshotDocument("other", "stream-1", 4, "other-r4"),
            });
        }

        private static BsonDocument CreateSnapshotDocument(string bucketId, string streamId, int streamRevision, string payload)
        {
            return new BsonDocument
            {
                [MongoSnapshotFields.Id] = new BsonDocument
                {
                    [MongoSnapshotFields.BucketId] = bucketId,
                    [MongoSnapshotFields.StreamId] = streamId,
                    [MongoSnapshotFields.StreamRevision] = streamRevision,
                },
                [MongoSnapshotFields.Payload] = payload,
            };
        }

        private ExplainPlanResult ExplainFind(
            IMongoCollection<BsonDocument> collection,
            BsonDocument filter,
            BsonDocument? sort = null,
            int? limit = null)
        {
            var explainCommand = new BsonDocument
            {
                ["explain"] = new BsonDocument
                {
                    ["find"] = collection.CollectionNamespace.CollectionName,
                    ["filter"] = filter,
                },
                ["verbosity"] = "executionStats",
            };

            if (sort != null)
            {
                explainCommand["explain"].AsBsonDocument["sort"] = sort;
            }

            if (limit.HasValue)
            {
                explainCommand["explain"].AsBsonDocument["limit"] = limit.Value;
            }

            var explain = collection.Database.RunCommand<BsonDocument>(explainCommand);
            var result = new ExplainPlanResult();
            WalkPlan(explain["queryPlanner"]["winningPlan"], result);

            var executionStats = explain["executionStats"].AsBsonDocument;
            result.TotalKeysExamined = executionStats["totalKeysExamined"].ToInt32();
            result.TotalDocsExamined = executionStats["totalDocsExamined"].ToInt32();
            return result;
        }

        private static void WalkPlan(BsonValue value, ExplainPlanResult result)
        {
            if (!value.IsBsonDocument)
            {
                return;
            }

            var document = value.AsBsonDocument;
            if (document.TryGetValue("stage", out BsonValue? stageValue))
            {
                result.Stages.Add(stageValue.AsString);
            }

            if (document.TryGetValue("indexName", out BsonValue? indexNameValue))
            {
                result.Indexes.Add(indexNameValue.AsString);
            }

            foreach (BsonElement element in document.Elements)
            {
                if (element.Value.IsBsonDocument)
                {
                    WalkPlan(element.Value, result);
                }
                else if (element.Value.IsBsonArray)
                {
                    foreach (BsonValue arrayItem in element.Value.AsBsonArray)
                    {
                        WalkPlan(arrayItem, result);
                    }
                }
            }
        }
    }
}