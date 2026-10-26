using System.Collections.Concurrent;
using FluentAssertions;
using global::MongoDB.Bson;
using global::MongoDB.Driver;
using global::MongoDB.Driver.Core.Events;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    public class Issue_87_recycle_reads_should_remain_selective : SpecificationBase
    {
        private const string Stream = "requested-stream";
        private readonly ConcurrentQueue<BsonDocument> _finds = new();
        private readonly List<BsonDocument> _documents = new();
        private readonly List<(string Description, BsonDocument Plan)> _plans = new();
        private MongoPersistenceEngine _engine = null!;
        private IMongoDatabase _database = null!;
        private MongoPersistenceOptions _options = null!;

        protected override void Context()
        {
            _ = new AcceptanceTestMongoPersistenceFactory();
            _options = new MongoPersistenceOptions
            {
                DisableSnapshotSupport = true,
                ConfigureClientSettingsAction = settings => settings.ClusterConfigurator = cluster =>
                    cluster.Subscribe<CommandStartedEvent>(command =>
                    {
                        if (command.CommandName == "find")
                            _finds.Enqueue(command.Command.DeepClone().AsBsonDocument);
                    })
            };
            var url = new MongoUrlBuilder(AcceptanceTestMongoPersistenceFactory.GetConnectionString())
            {
                DatabaseName = $"issue87-indexes-{Guid.NewGuid():N}"
            };
            _database = _options.ConnectToDatabase(url.ToString());
            _engine = new MongoPersistenceEngine(_database, new DocumentObjectSerializer(), _options);
            _engine.Initialize();

            // One legacy history and three new deletion attempts, including two from the
            // same original bucket. Original identities/revisions deliberately overlap.
            AddHistory(MongoSystemBuckets.RecycleBin, Stream, null, null);
            for (int lifetime = 0; lifetime < 3; lifetime++)
                AddHistory(MongoSystemBuckets.RecycleBin, $"deleted-{lifetime}", Stream, $"bucket-{lifetime % 2}");
            AddHistory(MongoSystemBuckets.RecycleBin, "new-only-token", "new-only", "bucket-0");
            AddHistory(MongoSystemBuckets.RecycleBin, "legacy-only", null, null);
            AddHistory("bucket-0", Stream, null, null);

            // Enough unrelated new/legacy recycled and live data to expose a bucket scan.
            for (int i = 0; i < 6000; i++)
            {
                AddDocument(MongoSystemBuckets.RecycleBin, $"new-token-{i}", $"unrelated-{i}", $"bucket-{i % 3}", 1);
                AddDocument(MongoSystemBuckets.RecycleBin, $"legacy-{i}", null, null, 1);
                AddDocument($"bucket-{i % 3}", $"live-{i}", null, null, 1);
            }
            _database.GetCollection<BsonDocument>("Commits").InsertMany(_documents);
        }

        protected override async Task BecauseAsync()
        {
            foreach (var range in new[] { (0, int.MaxValue), (32, 49), (31, int.MaxValue), (0, 50), (100, 110) })
                await ReadAndExplain(MongoSystemBuckets.RecycleBin, Stream, range.Item1, range.Item2);
            await ReadAndExplain(MongoSystemBuckets.RecycleBin, "new-only", 31, 50);
            await ReadAndExplain(MongoSystemBuckets.RecycleBin, "legacy-only", 31, 50);
            await ReadAndExplain(MongoSystemBuckets.RecycleBin, "absent-stream", 0, int.MaxValue);
            await ReadAndExplain("bucket-0", Stream, 31, 50);
        }

        [Fact]
        public void Full_sync_and_async_queries_examine_only_the_requested_histories()
        {
            foreach (var (description, plan) in _plans)
                Console.WriteLine($"{description}: {plan.ToJson()}");
            foreach (var (description, plan) in _plans)
            {
                var stats = plan["executionStats"];
                stats["executionSuccess"].AsBoolean.Should().BeTrue(description);
                // Allow boundary seeks and FETCH of legacy candidates, not thousands of
                // unrelated recycled records. Do not couple this to a specific plan name.
                stats["totalDocsExamined"].ToInt64().Should().BeLessThanOrEqualTo(400, description);
                stats["totalKeysExamined"].ToInt64().Should().BeLessThanOrEqualTo(400, description);
                var stages = Walk(plan["queryPlanner"]["winningPlan"])
                    .Where(node => node.Contains("stage")).Select(node => node["stage"].AsString);
                stages.Should().NotContain("COLLSCAN", description);
                stages.Should().NotContain("SORT", description + ": revision order should come from the indexes");
            }
        }

        [Fact]
        public void Both_recycle_identity_branches_have_equality_index_bounds()
        {
            foreach (var (_, plan) in _plans.Take(2))
            {
                var bounds = Walk(plan["queryPlanner"]["winningPlan"])
                    .Where(node => node.Contains("indexBounds"))
                    .Select(node => node["indexBounds"].AsBsonDocument).ToList();
                foreach (var field in new[] { MongoCommitFields.OriginalStreamId, MongoCommitFields.StreamId })
                    bounds.Should().Contain(bound => bound.Contains(field) &&
                        bound[field].AsBsonArray.Contains(new BsonString($"[\"{Stream}\", \"{Stream}\"]")),
                        $"the {field} branch must seek the requested identity, not scan the recycle bucket");
            }
        }

        private async Task ReadAndExplain(string bucket, string stream, int min, int max)
        {
            var expected = _documents.Where(document => document[MongoCommitFields.BucketId] == bucket &&
                (bucket == MongoSystemBuckets.RecycleBin && document.Contains(MongoCommitFields.OriginalStreamId)
                    ? document[MongoCommitFields.OriginalStreamId] == stream : document[MongoCommitFields.StreamId] == stream) &&
                document[MongoCommitFields.StreamRevisionTo].AsInt32 >= min &&
                document[MongoCommitFields.StreamRevisionFrom].AsInt32 <= max)
                .Select(document => document[MongoCommitFields.CheckpointNumber].ToInt64()).ToList();
            BsonDocument? syncFind = null;
            foreach (bool async in new[] { false, true })
            {
                while (_finds.TryDequeue(out _)) { }
                IList<ICommit> commits;
                if (async)
                {
                    var observer = new CommitStreamObserver();
                    await _engine.GetFromAsync(bucket, stream, min, max, observer, CancellationToken.None);
                    commits = observer.Commits.ToList();
                }
                else
                    commits = _engine.GetFrom(bucket, stream, min, max).ToList();

                commits.Select(commit => commit.CheckpointToken).Should().BeEquivalentTo(expected);
                commits.Should().OnlyContain(commit => commit.StreamId == stream && commit.BucketId == bucket);
                commits.Select(commit => commit.StreamRevision).Should().BeInAscendingOrder();
                _finds.Count.Should().Be(1, "explain must use the actual complete engine query");
                _finds.TryDequeue(out var command).Should().BeTrue();
                var find = new BsonDocument
                {
                    { "find", command!["find"] }, { "filter", command["filter"] }, { "sort", command["sort"] }
                };
                find["sort"].Should().Be(new BsonDocument(MongoCommitFields.StreamRevisionFrom, 1));
                if (async)
                    find.Equals(syncFind).Should().BeTrue("sync and async must send equivalent filters and sorts");
                else
                    syncFind = find;

                var plan = await _database.RunCommandAsync<BsonDocument>(new BsonDocument
                {
                    { "explain", find }, { "verbosity", "executionStats" }
                });
                plan["executionStats"]["nReturned"].ToInt32().Should().Be(expected.Count);
                var description = $"{bucket}/{stream} [{min}, {max}], async={async}";
                _plans.Add((description, plan));
            }
        }

        private void AddHistory(string bucket, string storedId, string? originalId, string? originalBucket)
        {
            for (int sequence = 1; sequence <= 40; sequence++)
                AddDocument(bucket, storedId, originalId, originalBucket, sequence);
        }

        private void AddDocument(string bucket, string storedId, string? originalId, string? originalBucket, int sequence)
        {
            var document = new BsonDocument
            {
                { MongoCommitFields.CheckpointNumber, (long)_documents.Count + 1 },
                { MongoCommitFields.BucketId, bucket }, { MongoCommitFields.StreamId, storedId },
                { MongoCommitFields.StreamRevisionFrom, sequence * 2 - 1 },
                { MongoCommitFields.StreamRevisionTo, sequence * 2 },
                { MongoCommitFields.CommitSequence, sequence },
                { MongoCommitFields.CommitId, new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.CSharpLegacy) },
                { MongoCommitFields.CommitStamp, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                { MongoCommitFields.Headers, new BsonDocument() }, { MongoCommitFields.Events, new BsonArray() }
            };
            if (originalId != null)
            {
                document[MongoCommitFields.OriginalStreamId] = originalId;
                document[MongoCommitFields.OriginalBucketId] = originalBucket;
            }
            _documents.Add(document);
        }

        private static IEnumerable<BsonDocument> Walk(BsonValue value)
        {
            if (value.IsBsonDocument)
            {
                yield return value.AsBsonDocument;
                foreach (var child in value.AsBsonDocument.Values.SelectMany(Walk))
                    yield return child;
            }
            else if (value.IsBsonArray)
                foreach (var child in value.AsBsonArray.SelectMany(Walk))
                    yield return child;
        }

        protected override void Cleanup()
        {
            _engine?.Dispose();
            if (_database != null)
                _database.Client.DropDatabase(_database.DatabaseNamespace.DatabaseName);
        }
    }
}
