using BenchmarkDotNet.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using NEventStore.Persistence.MongoDB;
using NEventStore.Persistence.MongoDB.Benchmark.Support;
using NEventStore.Persistence.MongoDB.Support;
using System;
using System.Collections.Generic;

namespace NEventStore.Persistence.MongoDB.Benchmark.Benchmarks
{
    /// <summary>
    /// Exercises duplicate commit-id and duplicate checkpoint retry paths without changing engine behavior.
    /// </summary>
    [Config(typeof(AllowNonOptimized))]
    [SimpleJob(launchCount: 3, warmupCount: 3, iterationCount: 3, invocationCount: 1)]
    [MemoryDiagnoser]
    [MeanColumn, StdErrorColumn, StdDevColumn, MinColumn, MaxColumn, IterationsColumn]
    public class DuplicateConflictBenchmarks
    {
        [Params(10, 100)]
        public int Iterations { get; set; }

        [Benchmark]
        public int DuplicateCommitIdPath()
        {
            var persistence = CreatePersistence();
            persistence.Purge();

            var duplicateCount = 0;
            for (int i = 0; i < Iterations; i++)
            {
                var streamId = Guid.NewGuid().ToString("N");
                var duplicateCommitId = Guid.NewGuid();

                persistence.Commit(CreateAttempt(streamId, streamRevision: 1, commitSequence: 1, duplicateCommitId, i));

                try
                {
                    persistence.Commit(CreateAttempt(streamId, streamRevision: 2, commitSequence: 2, duplicateCommitId, i + 1));
                }
                catch (DuplicateCommitException)
                {
                    duplicateCount++;
                }
            }

            return duplicateCount;
        }

        [Benchmark]
        public int DuplicateCheckpointRetryPath()
        {
            var options = new MongoPersistenceOptions();
            var db = options.ConnectToDatabase(EventStoreHelpers.GetConnectionString());
            var collection = db.GetCollection<BsonDocument>("Commits");
            options.CheckpointGenerator = new OneDuplicateCheckpointGenerator(collection);

            var persistence = CreatePersistence(options);
            persistence.Purge();

            var successfulCommits = 0;
            for (int i = 0; i < Iterations; i++)
            {
                // Seed checkpoint 1 so the next commit first attempt conflicts and forces SignalDuplicateId/Next retry.
                var seedStreamId = $"seed-{i}";
                persistence.Commit(CreateAttempt(seedStreamId, streamRevision: 1, commitSequence: 1, Guid.NewGuid(), i));

                var writeStreamId = $"retry-{i}";
                var commit = persistence.Commit(CreateAttempt(writeStreamId, streamRevision: 1, commitSequence: 1, Guid.NewGuid(), i + 1));
                if (commit != null)
                {
                    successfulCommits++;
                }
            }

            return successfulCommits;
        }

        private static IPersistStreams CreatePersistence(MongoPersistenceOptions? options = null)
        {
            var eventStore = EventStoreHelpers.WireupEventStore(options);
            return (IPersistStreams)eventStore.Advanced;
        }

        private static CommitAttempt CreateAttempt(string streamId, int streamRevision, int commitSequence, Guid commitId, int value)
        {
            return new CommitAttempt(
                bucketId: Bucket.Default,
                streamId: streamId,
                streamRevision: streamRevision,
                commitId: commitId,
                commitSequence: commitSequence,
                commitStamp: DateTime.UtcNow,
                headers: null,
                events: new List<EventMessage>
                {
                    new EventMessage { Body = new SomeDomainEvent { Value = value.ToString() } }
                }
            );
        }

        private sealed class OneDuplicateCheckpointGenerator : ICheckpointGenerator
        {
            private readonly InMemoryCheckpointGenerator _inner;
            private bool _shouldDuplicateOnNext = true;

            public OneDuplicateCheckpointGenerator(IMongoCollection<BsonDocument> collection)
            {
                _inner = new InMemoryCheckpointGenerator(collection);
            }

            public long Next()
            {
                if (_shouldDuplicateOnNext)
                {
                    _shouldDuplicateOnNext = false;
                    return 1;
                }

                return _inner.Next();
            }

            public async System.Threading.Tasks.Task<long> NextAsync(System.Threading.CancellationToken cancellationToken = default)
            {
                if (_shouldDuplicateOnNext)
                {
                    _shouldDuplicateOnNext = false;
                    return 1;
                }

                return await _inner.NextAsync(cancellationToken).ConfigureAwait(false);
            }

            public void SignalDuplicateId(long id)
            {
                _inner.SignalDuplicateId(id);
                _shouldDuplicateOnNext = false;
            }

            public async System.Threading.Tasks.Task SignalDuplicateIdAsync(long id, System.Threading.CancellationToken cancellationToken = default)
            {
                await _inner.SignalDuplicateIdAsync(id, cancellationToken).ConfigureAwait(false);
                _shouldDuplicateOnNext = false;
            }
        }
    }
}
