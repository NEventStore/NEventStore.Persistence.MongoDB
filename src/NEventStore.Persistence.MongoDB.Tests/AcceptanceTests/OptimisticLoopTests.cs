﻿using System.Diagnostics;
using NEventStore.PollingClient;
using NEventStore.Persistence.AcceptanceTests;
using NEventStore.Persistence.AcceptanceTests.BDD;
using FluentAssertions;
#if MSTEST
using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
#if NUNIT
using NUnit.Framework;
#endif
#if XUNIT
using Xunit;
using Xunit.Should;
#endif

namespace NEventStore.Persistence.MongoDB.Tests.AcceptanceTests
{
    public class Observer : IObserver<ICommit>
    {
        private int _counter;

        public int Counter
        {
            get { return _counter; }
        }

        private long _lastCheckpoint;

        public void OnNext(ICommit value)
        {
            var checkpoint = value.CheckpointToken;
            if (checkpoint > _lastCheckpoint)
                _counter++;

            _lastCheckpoint = checkpoint;
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_a_reader_observe_commits_from_a_lot_of_writers : SpecificationBase
    {
        protected const int IterationsPerWriter = 40;
        protected const int ParallelWriters = 8;
        protected const int PollingInterval = 100;
        private readonly List<IPersistStreams> _writers = [];
        private PollingClient2? _client;
        private Observer? _observer;

        protected override void Context()
        {
            for (int c = 1; c <= ParallelWriters; c++)
            {
                var client = new AcceptanceTestMongoPersistenceFactory().Build();

                if (c == 1)
                {
                    client.Drop();
                }
                client.Initialize();
                _writers.Add(client);
            }

            _observer = new Observer();

            var reader = new AcceptanceTestMongoPersistenceFactory().Build();
            _client = new PollingClient2(reader, c =>
            {
                _observer.OnNext(c);
                return PollingClient2.HandlingResult.MoveToNext;
            }, PollingInterval);

            _client.StartFrom(0);
        }

        protected override void Because()
        {
            var start = new ManualResetEventSlim(false);
            var stop = new ManualResetEventSlim(false);
            long counter = 0;
            var rnd = new Random(DateTime.Now.Millisecond);

            for (int t = 0; t < ParallelWriters; t++)
            {
                int t1 = t;
                var runner = new Thread(() =>
                {
                    start.Wait();
                    for (int c = 0; c < IterationsPerWriter; c++)
                    {
                        try
                        {
                            _writers[t1].Commit(Guid.NewGuid().ToString().BuildAttempt());
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                            stop.Set();
                            //throw;
                        }
                        Thread.Sleep(rnd.Next(2));
                    }
                    var current = Interlocked.Increment(ref counter);
                    Console.WriteLine("Thread {0} completed. {1} done.", t1, current);
                    if (current == ParallelWriters)
                    {
                        stop.Set();
                    }
                });

                runner.Start();
            }

            start.Set();
            stop.Wait(3 * 60 * 1000);

            Thread.Sleep(1500);
            _client!.Dispose();
        }

        [Fact]
        public void Should_never_miss_a_commit()
        {
            _observer!.Counter.Should().Be(IterationsPerWriter * ParallelWriters);
        }

        protected override void Cleanup()
        {
            for (int c = 0; c < ParallelWriters; c++)
            {
                if (c == ParallelWriters - 1)
                    _writers[c].Drop();

                _writers[c].Dispose();
            }
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_first_commit_is_persisted : PersistenceEngineConcern
    {
        private ICommit? _commit;

        protected override void Context()
        {
        }

        protected override void Because()
        {
            _commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
        }

        [Fact]
        public void Should_have_checkpoint_equal_to_one()
        {
            Assert.That(_commit, Is.Not.Null);
            _commit!.CheckpointToken.Should().Be(1);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_second_commit_is_persisted : PersistenceEngineConcern
    {
        private ICommit? _commit;

        protected override void Context()
        {
            Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
        }

        protected override void Because()
        {
            _commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
        }

        [Fact]
        public void Should_have_checkpoint_equal_to_two()
        {
            Assert.That(_commit, Is.Not.Null);
            _commit!.CheckpointToken.Should().Be(2);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_commit_is_persisted_after_a_stream_deletion : PersistenceEngineConcern
    {
        private ICommit? _commit;

        protected override void Context()
        {
            var commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            Persistence.DeleteStream(commit!.BucketId, commit.StreamId);
        }

        protected override void Because()
        {
            _commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
        }

        [Fact]
        public void Should_have_checkpoint_equal_to_two()
        {
            Assert.That(_commit, Is.Not.Null);
            _commit!.CheckpointToken.Should().Be(2);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_commit_is_persisted_after_concurrent_insertions_and_deletions : PersistenceEngineConcern
    {
        private const int Iterations = 10;
        private const int Clients = 10;
        private Int64 _checkpointToken;

        protected override void Context()
        {
            var lazyInitializer = Persistence;

            var start = new ManualResetEventSlim(false);
            var stop = new ManualResetEventSlim(false);
            int counter = 0;

            for (int c = 0; c < Clients; c++)
            {
                new Thread(() =>
                {
                    start.Wait();

                    for (int i = 0; i < Iterations; i++)
                    {
                        try
                        {
                            var commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
                            Persistence.DeleteStream(commit!.BucketId, commit.StreamId);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                            stop.Set();
                        }
                    }

                    Interlocked.Increment(ref counter);
                    if (counter >= Clients)
                        stop.Set();
                }).Start();
            }

            start.Set();
            stop.Wait();
        }

        protected override void Because()
        {
            _checkpointToken = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt())!.CheckpointToken;
        }

        [Fact]
        public void Should_have_correct_checkpoint()
        {
            _checkpointToken.Should().Be((Clients * Iterations) + 1);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_a_stream_is_deleted : PersistenceEngineConcern
    {
        private ICommit? _commit;

        protected override void Context()
        {
            _commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
        }

        protected override void Because()
        {
            Persistence.DeleteStream(_commit!.BucketId, _commit.StreamId);
        }

        [Fact]
        public void The_commits_cannot_be_loaded_from_the_stream()
        {
            Persistence.GetFrom(_commit!.StreamId, int.MinValue, int.MaxValue).Should().BeEmpty();
        }

        [Fact]
        public void The_commits_cannot_be_loaded_from_the_bucket_and_checkpoint()
        {
            Persistence.GetFrom(_commit!.BucketId, 0).Should().BeEmpty();
        }

        [Fact]
        public void The_commits_cannot_be_loaded_from_the_checkpoint()
        {
            Persistence.GetFrom(0).Should().BeEmpty();
        }

        [Fact]
        public void The_commits_cannot_be_loaded_from_bucket_and_start_date()
        {
            Persistence.GetFrom(_commit!.BucketId, DateTime.MinValue).Should().BeEmpty();
        }

        [Fact]
        public void The_commits_cannot_be_loaded_from_bucket_and_date_range()
        {
            Persistence.GetFromTo(_commit!.BucketId, DateTime.MinValue, DateTime.MaxValue).Should().BeEmpty();
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_deleted_streams_are_purged_and_last_commit_is_marked_as_deleted : PersistenceEngineConcern
    {
        private ICommit[]? _commits;

        protected override void Context()
        {
            Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            var commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            Persistence.DeleteStream(commit!.BucketId, commit.StreamId);
            Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            Persistence.DeleteStream(commit!.BucketId, commit.StreamId);
        }

        protected override void Because()
        {
#if NET472_OR_GREATER
            MongoPersistenceEngine mongoEngine;
            if (Persistence is NEventStore.Diagnostics.PerformanceCounterPersistenceEngine performanceCounterPersistenceEngine)
            {
                mongoEngine = (MongoPersistenceEngine)(performanceCounterPersistenceEngine.UnwrapPersistenceEngine());
            }
            else
            {
                mongoEngine = (MongoPersistenceEngine)Persistence;
            }
#else
            var mongoEngine = (MongoPersistenceEngine)Persistence;
#endif
            mongoEngine.EmptyRecycleBin();
            _commits = mongoEngine.GetDeletedCommits().ToArray();
        }

        [Fact]
        public void Last_deleted_commit_is_not_purged_to_preserve_checkpoint_numbering()
        {
            Assert.That(_commits, Is.Not.Null);
            _commits!.Length.Should().Be(1);
        }

        [Fact]
        public void Last_deleted_commit_has_the_higher_checkpoint_number()
        {
            Assert.That(_commits, Is.Not.Null);
            _commits![0].CheckpointToken.Should().Be(4);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_deleted_streams_are_purged : PersistenceEngineConcern
    {
        private ICommit[]? _commits;

        protected override void Context()
        {
            Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            var commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            Persistence.DeleteStream(commit!.BucketId, commit.StreamId);
            commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            Persistence.DeleteStream(commit!.BucketId, commit.StreamId);
            Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
        }

        protected override void Because()
        {
#if NET472_OR_GREATER
            MongoPersistenceEngine mongoEngine;
            if (Persistence is NEventStore.Diagnostics.PerformanceCounterPersistenceEngine performanceCounterPersistenceEngine)
            {
                mongoEngine = (MongoPersistenceEngine)(performanceCounterPersistenceEngine.UnwrapPersistenceEngine());
            }
            else
            {
                mongoEngine = (MongoPersistenceEngine)Persistence;
            }
#else
            var mongoEngine = (MongoPersistenceEngine)Persistence;
#endif
            mongoEngine.EmptyRecycleBin();
            _commits = mongoEngine.GetDeletedCommits().ToArray();
        }

        [Fact]
        public void All_deleted_commits_are_purged()
        {
            Assert.That(_commits, Is.Not.Null);
            _commits!.Length.Should().Be(0);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_stream_is_added_after_a_bucket_purge : PersistenceEngineConcern
    {
        private Int64 _checkpointBeforePurge;
        private Int64 _checkpointAfterPurge;

        protected override void Context()
        {
            var commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            _checkpointBeforePurge = commit!.CheckpointToken;
            Persistence.DeleteStream(commit.StreamId);
            Persistence.Purge("default");
        }

        protected override void Because()
        {
            var commit = Persistence.Commit(Guid.NewGuid().ToString().BuildAttempt());
            _checkpointAfterPurge = commit!.CheckpointToken;
        }

        [Fact]
        public void Checkpoint_number_must_be_greater_than()
        {
            _checkpointAfterPurge.Should().BeGreaterThan(_checkpointBeforePurge);
        }
    }

#if MSTEST
    [TestClass]
#endif
    public class When_a_stream_with_two_or_more_commits_is_deleted : PersistenceEngineConcern
    {
        private string? _streamId;
        private string? _bucketId;

        protected override void Context()
        {
            _streamId = Guid.NewGuid().ToString();
            var commit = Persistence.Commit(_streamId.BuildAttempt());
            _bucketId = commit!.BucketId;

            Persistence.Commit(commit.BuildNextAttempt());
        }

        protected override void Because()
        {
            Persistence.DeleteStream(_bucketId!, _streamId!);
        }

        [Fact]
        public void All_commits_are_deleted()
        {
            var commits = Persistence.GetFrom(_bucketId!, _streamId!, int.MinValue, int.MaxValue).ToArray();

            commits.Length.Should().Be(0);
        }
    }
}
