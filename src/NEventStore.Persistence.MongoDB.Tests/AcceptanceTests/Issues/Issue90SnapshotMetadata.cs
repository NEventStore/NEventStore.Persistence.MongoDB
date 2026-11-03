using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using NEventStore.Serialization;
#if NUNIT
using NUnit.Framework;

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests.Issues
{
    [TestFixture(false, 0)]
    [TestFixture(true, 0)]
    [TestFixture(false, 1)]
    [TestFixture(true, 1)]
    [TestFixture(false, 2)]
    [TestFixture(true, 2)]
    [TestFixture(false, 4)]
    [TestFixture(true, 4)]
    [TestFixture(false, 5)]
    [TestFixture(true, 5)]
    [TestFixture(false, 3)]
    [TestFixture(true, 3)]
    public class when_Issue_90_snapshot_metadata_overlaps_another_write : SpecificationBase
    {
        private readonly bool _async;
        // Commit before metadata, newer snapshot, older snapshot, commit after metadata.
        private readonly int _interleaving;
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private MongoPersistenceEngine _snapshotEngine = null!;
        private MongoPersistenceEngine _otherEngine = null!;
        private IMongoDatabase _database = null!;
        private CommitAttempt _last = null!;
        private Task<bool>? _snapshot;
        private bool _saved;
        private int _armed;
        private const string Stream = "snapshot-race";
        private const string BucketId = "snapshot-bucket";
        private int ExpectedHead => _interleaving is 0 or 3 ? 6 : 4;
        private int ExpectedSnapshot => _interleaving == 1 ? 4 : 2;

        public when_Issue_90_snapshot_metadata_overlaps_another_write(bool async, int interleaving)
        {
            _async = async;
            _interleaving = interleaving;
        }

        protected override void Context()
        {
            _ = new AcceptanceTestMongoPersistenceFactory();
            var url = new MongoUrl(AcceptanceTestMongoPersistenceFactory.GetConnectionString());
            var settings = MongoClientSettings.FromUrl(url);
            settings.ClusterConfigurator = cluster => cluster.Subscribe<CommandStartedEvent>(command =>
            {
                if (command.CommandName != "update" || command.Command["update"] != "Streams") return;
                var update = command.Command["updates"][0]["u"];
                var fields = update.IsBsonArray ? update[0]["$set"] : update.AsBsonDocument.GetValue("$set", new BsonDocument());
                if (!fields.AsBsonDocument.Contains(MongoStreamHeadFields.SnapshotRevision)
                    || Interlocked.Exchange(ref _armed, 0) != 1) return;
                _entered.Set();
                if (!_release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Snapshot metadata was not released.");
            });
            var name = $"issue90-{Guid.NewGuid():N}";
            _database = new MongoClient(url).GetDatabase(name);
            var options = new AcknowledgedMetadataOptions { PersistStreamHeadsOnBackgroundThread = false };
            _otherEngine = new MongoPersistenceEngine(_database, new DocumentObjectSerializer(), options);
            _snapshotEngine = new MongoPersistenceEngine(new MongoClient(settings).GetDatabase(name), new DocumentObjectSerializer(), options);
            _otherEngine.Initialize();
            _snapshotEngine.Initialize();
            _last = _otherEngine.CommitMany(2, Stream, BucketId).Last();
        }

        protected override async Task BecauseAsync()
        {
            Interlocked.Exchange(ref _armed, 1);
            _snapshot = Task.Run(() => Save(_snapshotEngine, 2));
            try
            {
                _entered.Wait(TimeSpan.FromSeconds(20)).Should().BeTrue("the snapshot must pause immediately before its stream metadata write");
                if (_interleaving == 0) await CommitNext();
                if (_interleaving == 1) (await Save(_otherEngine, 4)).Should().BeTrue();
                if (_interleaving == 2) (await Save(_otherEngine, 1)).Should().BeTrue();
            }
            finally { _release.Set(); }
            _saved = await _snapshot;
            if (_interleaving == 3) await CommitNext();
            if (_interleaving == 4) (await Save(_otherEngine, 1)).Should().BeTrue();
            if (_interleaving == 5) (await Save(_otherEngine, 2)).Should().BeTrue();
        }

        private async Task CommitNext()
        {
            var attempt = _last.BuildNextAttempt();
            if (_async) await _otherEngine.CommitAsync(attempt, CancellationToken.None);
            else _otherEngine.Commit(attempt);
        }

        private Task<bool> Save(MongoPersistenceEngine engine, int revision)
        {
            var snapshot = new Snapshot(BucketId, Stream, revision, "snapshot");
            return _async ? engine.AddSnapshotAsync(snapshot, CancellationToken.None) : Task.FromResult(engine.AddSnapshot(snapshot));
        }

        [Fact]
        public void Metadata_preserves_commits_and_the_highest_snapshot_revision()
        {
            _saved.Should().BeTrue();
            var head = _database.GetCollection<BsonDocument>("Streams").Find(FilterDefinition<BsonDocument>.Empty).Single();
            head[MongoStreamHeadFields.HeadRevision].AsInt32.Should().Be(ExpectedHead);
            head[MongoStreamHeadFields.SnapshotRevision].AsInt32.Should().Be(ExpectedSnapshot);
            head[MongoStreamHeadFields.Unsnapshotted].AsInt32.Should().Be(ExpectedHead - ExpectedSnapshot);
        }

        [Fact]
        public async Task Public_candidate_queries_include_exactly_the_unsnapshotted_events()
        {
            var count = ExpectedHead - ExpectedSnapshot;
            var candidates = await Candidates(Math.Max(1, count));
            if (count == 0) candidates.Should().BeEmpty();
            else
            {
                candidates.Should().ContainSingle();
                candidates[0].HeadRevision.Should().Be(ExpectedHead);
                candidates[0].SnapshotRevision.Should().Be(ExpectedSnapshot);
            }
            (await Candidates(count + 1)).Should().BeEmpty();
        }

        private async Task<IList<IStreamHead>> Candidates(int threshold)
        {
            if (!_async) return _snapshotEngine.GetStreamsToSnapshot(BucketId, threshold).ToList();
            var observer = new StreamHeadObserver();
            await _snapshotEngine.GetStreamsToSnapshotAsync(BucketId, threshold, observer, CancellationToken.None);
            return observer.Heads;
        }

        protected override void Cleanup()
        {
            _release.Set();
            _snapshot?.GetAwaiter().GetResult();
            _snapshotEngine?.Dispose();
            _otherEngine?.Dispose();
            if (_database != null) _database.Client.DropDatabase(_database.DatabaseNamespace.DatabaseName);
            _entered.Dispose();
            _release.Dispose();
        }

        private sealed class StreamHeadObserver : IAsyncObserver<IStreamHead>
        {
            public IList<IStreamHead> Heads { get; } = new List<IStreamHead>();
            public Task<bool> OnNextAsync(IStreamHead value, CancellationToken cancellationToken = default)
            { Heads.Add(value); return Task.FromResult(true); }
            public Task OnErrorAsync(Exception ex, CancellationToken cancellationToken = default) => Task.FromException(ex);
            public Task OnCompletedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private sealed class AcknowledgedMetadataOptions : MongoPersistenceOptions
        {
            public override MongoCollectionSettings GetStreamSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
            public override MongoCollectionSettings GetSnapshotSettings() => new() { WriteConcern = WriteConcern.Acknowledged };
        }
    }
}
#endif
