﻿#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA2254 // Template should be a static expression

using global::MongoDB.Bson;
using global::MongoDB.Driver;
using Microsoft.Extensions.Logging;
using NEventStore.Logging;
using NEventStore.Serialization;
using NEventStore.Persistence.MongoDB.Support;
using System.Runtime.ExceptionServices;

namespace NEventStore.Persistence.MongoDB
{
    /// <summary>
    /// Represents a persistence engine that uses MongoDB to store commits and snapshots.
    /// </summary>
    public partial class MongoPersistenceEngine : IPersistStreams
    {
        private const string ConcurrencyException = "E1100";
        private static readonly ILogger Logger = LogFactory.BuildLogger(typeof(MongoPersistenceEngine));
        private readonly IDocumentSerializer _serializer;
        private int _initialized;
        private readonly MongoPersistenceOptions _options;
        private readonly string _systemBucketName;
        private ICheckpointGenerator? _checkpointGenerator;
        private static readonly SortDefinition<BsonDocument> SortByAscendingCheckpointNumber = Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.CheckpointNumber);
        private readonly static UpdateOptions UpsertUpdateOptions = new() { IsUpsert = true };

        /// <summary>
        /// Initializes a new instance of the <see cref="MongoPersistenceEngine"/> class.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public MongoPersistenceEngine(
            IMongoDatabase store,
            IDocumentSerializer serializer,
            MongoPersistenceOptions options)
        {
            var db = store ?? throw new ArgumentNullException(nameof(store));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _systemBucketName = options.SystemBucketName;

            // set config options
            var _commitSettings = _options.GetCommitSettings();
            var _snapshotSettings = _options.GetSnapshotSettings();
            var _streamSettings = _options.GetStreamSettings();
            var _insertCommitWriteConcern = _options.GetInsertCommitWriteConcern();

            // from the docs: IMongoCollection is thread safe and safe to be stored globally
            PersistedCommits = db.GetCollection<BsonDocument>("Commits", _commitSettings).WithWriteConcern(_insertCommitWriteConcern);
            PersistedStreamHeads = db.GetCollection<BsonDocument>("Streams", _streamSettings);
            PersistedSnapshots = db.GetCollection<BsonDocument>("Snapshots", _snapshotSettings);
        }

        /// <summary>
        /// Gets the collection of commits.
        /// </summary>
        protected virtual IMongoCollection<BsonDocument> PersistedCommits { get; }

        /// <summary>
        /// Gets the collection of stream heads.
        /// </summary>
        protected virtual IMongoCollection<BsonDocument> PersistedStreamHeads { get; }

        /// <summary>
        /// Gets the collection of snapshots.
        /// </summary>
        protected virtual IMongoCollection<BsonDocument> PersistedSnapshots { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public virtual void Initialize()
        {
            if (Interlocked.Increment(ref _initialized) > 1)
            {
                return;
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.InitializingStorage);
            }

            TryMongo(() =>
            {
                PersistedCommits.Indexes.CreateOne(
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MongoCommitFields.BucketId)
                            .Ascending(MongoCommitFields.CheckpointNumber),
                        new CreateIndexOptions()
                        {
                            Name = MongoCommitIndexes.GetFromCheckpoint,
                            Unique = true
                        }
                    )
                );

                PersistedCommits.Indexes.CreateOne(
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MongoCommitFields.BucketId)
                            .Ascending(MongoCommitFields.StreamId)
                            .Ascending(MongoCommitFields.StreamRevisionFrom)
                            .Ascending(MongoCommitFields.StreamRevisionTo),
                        new CreateIndexOptions()
                        {
                            Name = MongoCommitIndexes.GetFrom,
                            Unique = true
                        }
                    )
                );

                PersistedCommits.Indexes.CreateOne(
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MongoCommitFields.BucketId)
                            .Ascending(MongoCommitFields.StreamId)
                            .Ascending(MongoCommitFields.CommitSequence),
                        new CreateIndexOptions()
                        {
                            Name = MongoCommitIndexes.LogicalKey,
                            Unique = true
                        }
                    )
                );

                PersistedCommits.Indexes.CreateOne(
                    new CreateIndexModel<BsonDocument>(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MongoCommitFields.CommitStamp),
                        new CreateIndexOptions()
                        {
                            Name = MongoCommitIndexes.CommitStamp,
                            Unique = false
                        }
                    )
                );

                PersistedCommits.Indexes.CreateOne(
                    new CreateIndexModel<BsonDocument>(
                       Builders<BsonDocument>.IndexKeys
                           .Ascending(MongoCommitFields.BucketId)
                           .Ascending(MongoCommitFields.StreamId)
                           .Ascending(MongoCommitFields.CommitId),
                       new CreateIndexOptions()
                       {
                           Name = MongoCommitIndexes.CommitId,
                           Unique = true
                       }
                   )
                );

                if (!_options.DisableSnapshotSupport)
                {
                    PersistedStreamHeads.Indexes.CreateOne(
                        new CreateIndexModel<BsonDocument>(
                            Builders<BsonDocument>.IndexKeys
                                .Ascending(MongoStreamHeadFields.Unsnapshotted),
                            new CreateIndexOptions()
                            {
                                Name = MongoStreamIndexes.Unsnapshotted,
                                Unique = false
                            }
                        )
                    );
                }

                _checkpointGenerator = _options.CheckpointGenerator ??
                    new AlwaysQueryDbForNextValueCheckpointGenerator(PersistedCommits);

                // EmptyRecycleBin();
            });
        }

        /// <inheritdoc/>
        public virtual IEnumerable<ICommit> GetFrom(string bucketId, string streamId, int minRevision, int maxRevision)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingAllCommitsBetween, streamId, bucketId, minRevision, maxRevision);
            }

            return TryMongo(() =>
            {
                var filters = new List<FilterDefinition<BsonDocument>>()
                {
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId),
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.StreamId, streamId)
                };
                if (minRevision > 0)
                {
                    filters.Add(
                        Builders<BsonDocument>.Filter.Gte(MongoCommitFields.StreamRevisionTo, minRevision)
                        );
                }
                if (maxRevision < int.MaxValue)
                {
                    filters.Add(
                        Builders<BsonDocument>.Filter.Lte(MongoCommitFields.StreamRevisionFrom, maxRevision)
                        );
                }

                var query = Builders<BsonDocument>.Filter.And(filters);

                return PersistedCommits
                    .Find(query)
                    // .Sort(Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.StreamRevisionFrom))
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToEnumerable()
                    .Select(mc => mc.ToCommit(_serializer));
            });
        }

        /// <inheritdoc/>
        [Obsolete("DateTime is problematic in distributed systems. Use GetFrom(Int64 checkpointToken) instead. This method will be removed in a later version.")]
        public virtual IEnumerable<ICommit> GetFrom(string bucketId, DateTime start)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingAllCommitsFrom, start, bucketId);
            }

            return TryMongo(() =>
            {
                var query = Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId);
                if (start != DateTime.MinValue)
                {
                    query = Builders<BsonDocument>.Filter.And(
                        query,
                        Builders<BsonDocument>.Filter.Gte(MongoCommitFields.CommitStamp, start)
                    );
                }

                return PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToEnumerable()
                    .Select(x => x.ToCommit(_serializer));
            });
        }

        /// <inheritdoc/>
        public virtual IEnumerable<ICommit> GetFrom(string bucketId, Int64 checkpointToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingAllCommitsFromBucketAndCheckpoint, bucketId, checkpointToken);
            }

            return TryMongo(() =>
            {
                var query = Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId);
                if (checkpointToken > 0)
                {
                    query = Builders<BsonDocument>.Filter.And(
                        query,
                        Builders<BsonDocument>.Filter.Gt(MongoCommitFields.CheckpointNumber, checkpointToken)
                    );
                }

                return PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToEnumerable()
                    .Select(x => x.ToCommit(_serializer));
            });
        }

        /// <inheritdoc/>
        public virtual IEnumerable<ICommit> GetFromTo(string bucketId, long fromCheckpointToken, long toCheckpointToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingCommitsFromBucketAndFromToCheckpoint, bucketId, fromCheckpointToken, toCheckpointToken);
            }

            return TryMongo(() =>
            {
                var filters = new List<FilterDefinition<BsonDocument>>()
                {
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId)
                };
                if (fromCheckpointToken > 0)
                {
                    filters.Add(
                        Builders<BsonDocument>.Filter.Gt(MongoCommitFields.CheckpointNumber, fromCheckpointToken)
                        );
                }
                if (toCheckpointToken < long.MaxValue)
                {
                    filters.Add(
                        Builders<BsonDocument>.Filter.Lte(MongoCommitFields.CheckpointNumber, toCheckpointToken)
                        );
                }

                var query = Builders<BsonDocument>.Filter.And(filters);

                return PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToEnumerable()
                    .Select(x => x.ToCommit(_serializer));
            });
        }

        /// <inheritdoc/>
        public virtual IEnumerable<ICommit> GetFrom(Int64 checkpointToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingAllCommitsFromCheckpoint, checkpointToken);
            }

            return TryMongo(() =>
            {
                var query = Builders<BsonDocument>.Filter.Ne(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin);
                if (checkpointToken > 0)
                {
                    query = Builders<BsonDocument>.Filter.And(
                        query,
                        Builders<BsonDocument>.Filter.Gt(MongoCommitFields.CheckpointNumber, checkpointToken)
                    );
                }

                return PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToEnumerable()
                    .Select(x => x.ToCommit(_serializer));
            });
        }

        /// <inheritdoc/>
        public virtual IEnumerable<ICommit> GetFromTo(long fromCheckpointToken, long toCheckpointToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingCommitsFromToCheckpoint, fromCheckpointToken, toCheckpointToken);
            }

            return TryMongo(() =>
            {
                var filters = new List<FilterDefinition<BsonDocument>>()
                {
                    Builders<BsonDocument>.Filter.Ne(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin)
                };
                if (fromCheckpointToken > 0)
                {
                    filters.Add(
                        Builders<BsonDocument>.Filter.Gt(MongoCommitFields.CheckpointNumber, fromCheckpointToken)
                        );
                }
                if (toCheckpointToken < long.MaxValue)
                {
                    filters.Add(
                        Builders<BsonDocument>.Filter.Lte(MongoCommitFields.CheckpointNumber, toCheckpointToken)
                        );
                }

                var query = Builders<BsonDocument>.Filter.And(filters);

                return PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToEnumerable()
                    .Select(x => x.ToCommit(_serializer));
            });
        }

        /// <inheritdoc/>
        [Obsolete("DateTime is problematic in distributed systems. Use GetFromTo(Int64 fromCheckpointToken, Int64 toCheckpointToken) instead. This method will be removed in a later version.")]
        public virtual IEnumerable<ICommit> GetFromTo(string bucketId, DateTime start, DateTime end)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingAllCommitsFromTo, start, end, bucketId);
            }

            return TryMongo(() =>
            {
                var filters = new List<FilterDefinition<BsonDocument>>()
                {
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId)
                };
                if (start > DateTime.MinValue)
                {
                    filters.Add(
                        Builders<BsonDocument>.Filter.Gte(MongoCommitFields.CommitStamp, start)
                        );
                }
                if (end < DateTime.MaxValue)
                {
                    filters.Add(
                        Builders<BsonDocument>.Filter.Lt(MongoCommitFields.CommitStamp, end)
                        );
                }

                var query = Builders<BsonDocument>.Filter.And(filters);

                return PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToEnumerable()
                    .Select(x => x.ToCommit(_serializer));
            });
        }

        /// <inheritdoc/>
        public virtual ICommit? Commit(CommitAttempt attempt)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.AttemptingToCommit, attempt.Events.Count, attempt.StreamId, attempt.CommitSequence);
            }

            return TryMongo(() =>
            {
                Int64 checkpointId = _checkpointGenerator!.Next();
                var commitDoc = attempt.ToMongoCommit(
                    checkpointId,
                    _serializer
                );

                bool retry = true;
                while (retry)
                {
                    try
                    {
                        // for concurrency / duplicate commit detection safe mode is required
                        PersistedCommits.InsertOne(commitDoc);

                        retry = false;
                        if (!_options.DisableSnapshotSupport)
                        {
                            UpdateStreamHeadInBackgroundThread(attempt.BucketId, attempt.StreamId, attempt.StreamRevision, attempt.Events.Count);
                        }

                        if (Logger.IsEnabled(LogLevel.Debug))
                        {
                            Logger.LogDebug(Messages.CommitPersisted, attempt.CommitId);
                        }
                    }
                    catch (MongoException e)
                    {
                        if (!e.Message.Contains(ConcurrencyException))
                        {
                            Logger.LogError(e, Messages.GenericPersistingError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId, e);
                            throw;
                        }

                        // checkpoint index? 
                        if (e.Message.Contains(MongoCommitIndexes.CheckpointNumberMMApV1)
                            || e.Message.Contains(MongoCommitIndexes.CheckpointNumberWiredTiger))
                        {
                            if (Logger.IsEnabled(LogLevel.Warning))
                            {
                                Logger.LogWarning(e, Messages.DuplicatedCheckpointTokenError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId);
                            }
                            _checkpointGenerator.SignalDuplicateId(checkpointId);
                            checkpointId = _checkpointGenerator.Next();
                            commitDoc[MongoCommitFields.CheckpointNumber] = checkpointId;
                        }
                        else
                        {
                            if (_options.ConcurrencyStrategy == ConcurrencyExceptionStrategy.FillHole)
                            {
                                FillHole(attempt, checkpointId);
                            }

                            if (e.Message.Contains(MongoCommitIndexes.CommitId))
                            {
                                var msg = String.Format(Messages.DuplicatedCommitError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId);
                                if (Logger.IsEnabled(LogLevel.Information))
                                {
                                    Logger.LogInformation(msg);
                                }
                                throw new DuplicateCommitException(msg);
                            }

                            ICommit? savedCommit = null;
                            var bsonSavedCommit = PersistedCommits
                                .FindSync(attempt.ToMongoCommitIdQuery())
                                .FirstOrDefault();
                            if (bsonSavedCommit != null)
                            {
                                savedCommit = bsonSavedCommit.ToCommit(_serializer);
                            }

                            if (savedCommit != null && savedCommit.CommitId == attempt.CommitId)
                            {
                                var msg = String.Format(Messages.DuplicatedCommitError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId);
                                if (Logger.IsEnabled(LogLevel.Information))
                                {
                                    Logger.LogInformation(msg);
                                }
                                throw new DuplicateCommitException(msg);
                            }

                            if (Logger.IsEnabled(LogLevel.Information))
                            {
                                Logger.LogInformation(Messages.ConcurrencyExceptionError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId, e);
                            }
                            throw new ConcurrencyException();
                        }
                    }
                }

                return commitDoc.ToCommit(_serializer);
            });
        }

        private void FillHole(CommitAttempt attempt, Int64 checkpointId)
        {
            try
            {
                var holeFillDoc = attempt.ToEmptyCommit(
                   checkpointId,
                   _systemBucketName
                );
                PersistedCommits.InsertOne(holeFillDoc);
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (Exception e)
            {
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning(e, Messages.FillHoleError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId, e);
                }
            }
        }

        /// <inheritdoc/>
        public virtual IEnumerable<IStreamHead> GetStreamsToSnapshot(string bucketId, int maxThreshold)
        {
            CheckIfSnapshotEnabled();

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingStreamsToSnapshot);
            }

            var result = TryMongo(() =>
            {
                var query = Builders<BsonDocument>.Filter.Gte(MongoStreamHeadFields.Unsnapshotted, maxThreshold);
                return PersistedStreamHeads
                    .Find(query)
                    .Sort(Builders<BsonDocument>.Sort.Descending(MongoStreamHeadFields.Unsnapshotted))
                    .ToEnumerable()
                    .Select(x => x.ToStreamHead());
            });
            return result ?? [];
        }

        /// <inheritdoc/>
        public virtual ISnapshot? GetSnapshot(string bucketId, string streamId, int maxRevision)
        {
            CheckIfSnapshotEnabled();

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingRevision, streamId, maxRevision);
            }

            return TryMongo(() =>
            {
                var query = ExtensionMethods.GetSnapshotQuery(bucketId, streamId, maxRevision);

                return PersistedSnapshots
                    .Find(query)
                    .Sort(Builders<BsonDocument>.Sort.Descending(MongoSnapshotFields.Id))
                    .Limit(1)
                    .ToEnumerable()
                    .Select(mc => mc.ToSnapshot(_serializer))
                    .FirstOrDefault();
            });
        }

        /// <inheritdoc/>
        public virtual bool AddSnapshot(ISnapshot snapshot)
        {
            CheckIfSnapshotEnabled();

            if (snapshot == null)
            {
                return false;
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.AddingSnapshot, snapshot.StreamId, snapshot.BucketId, snapshot.StreamRevision);
            }

            try
            {
                BsonDocument mongoSnapshot = snapshot.ToMongoSnapshot(_serializer);
                var query = Builders<BsonDocument>.Filter.Eq(MongoSnapshotFields.Id, mongoSnapshot[MongoSnapshotFields.Id]);
                var update = Builders<BsonDocument>.Update.Set(MongoSnapshotFields.Payload, mongoSnapshot[MongoSnapshotFields.Payload]);

                // Doing an upsert instead of an insert allows us to overwrite an existing snapshot and not get stuck with a
                // stream that needs to be snapshotted because the insert fails and the SnapshotRevision isn't being updated.
                PersistedSnapshots.UpdateOne(query, update, UpsertUpdateOptions);

                // More commits could have been made between us deciding that a snapshot is required and writing it so just
                // resetting the Un-snapshotted count may be a little off. Adding snapshots should be a separate process so
                // this is a good chance to make sure the numbers are still in-sync - it only adds a 'read' after all ...
                BsonDocument streamHeadId = GetStreamHeadId(snapshot.BucketId, snapshot.StreamId);
                StreamHead streamHead = PersistedStreamHeads.Find(Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.Id, streamHeadId))
                    .First()
                    .ToStreamHead();

                int unsnapshotted = streamHead.HeadRevision - snapshot.StreamRevision;

                PersistedStreamHeads.UpdateOne(
                    Builders<BsonDocument>.Filter
                        .Eq(MongoStreamHeadFields.Id, streamHeadId),
                    Builders<BsonDocument>.Update
                        .Set(MongoStreamHeadFields.SnapshotRevision, snapshot.StreamRevision)
                        .Set(MongoStreamHeadFields.Unsnapshotted, unsnapshotted)
                );

                return true;
            }
            catch (Exception e)
            {
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning(e, Messages.AddingSnapshotError, snapshot.StreamId, snapshot.BucketId, snapshot.StreamRevision, e);
                }
                return false;
            }
        }

        /// <inheritdoc/>
        public virtual void Purge()
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning(Messages.PurgingStorage);
            }
            TryMongo(() =>
            {
                PersistedCommits.DeleteMany(Builders<BsonDocument>.Filter.Empty);
                PersistedStreamHeads.DeleteMany(Builders<BsonDocument>.Filter.Empty);
                PersistedSnapshots.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            });
        }

        /// <inheritdoc/>
        public void Purge(string bucketId)
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning(Messages.PurgingBucket, bucketId);
            }
            TryMongo(() =>
            {
                PersistedStreamHeads.DeleteMany(Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.FullQualifiedBucketId, bucketId));
                PersistedSnapshots.DeleteMany(Builders<BsonDocument>.Filter.Eq(MongoSnapshotFields.FullQualifiedBucketId, bucketId));
                PersistedCommits.DeleteMany(Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId));
            });
        }

        /// <inheritdoc/>
        public void Drop()
        {
            Purge();
        }

        /// <inheritdoc/>
        public void DeleteStream(string bucketId, string streamId)
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning(Messages.DeletingStream, streamId, bucketId);
            }

            TryMongo(() =>
            {
                PersistedStreamHeads.DeleteOne(
                    Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.Id, new BsonDocument{
                        {MongoStreamHeadFields.BucketId, bucketId},
                        {MongoStreamHeadFields.StreamId, streamId}
                    })
                );

                PersistedSnapshots.DeleteMany(
                    Builders<BsonDocument>.Filter.Eq(MongoSnapshotFields.Id, new BsonDocument{
                        {MongoSnapshotFields.BucketId, bucketId},
                        {MongoSnapshotFields.StreamId, streamId}
                    })
                );

                PersistedCommits.UpdateMany(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId),
                        Builders<BsonDocument>.Filter.Eq(MongoCommitFields.StreamId, streamId)
                    ),
                    Builders<BsonDocument>.Update.Set(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin)
                );
            });
        }

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || IsDisposed)
            {
                return;
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.ShuttingDownPersistence);
            }
            IsDisposed = true;
        }

        private void UpdateStreamHeadInBackgroundThread(string bucketId, string streamId, int streamRevision, int eventsCount)
        {
            StartBackgroundThread(() =>
            {
                try
                {
                    BsonDocument streamHeadId = GetStreamHeadId(bucketId, streamId);
                    PersistedStreamHeads.UpdateOne(
                        Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.Id, streamHeadId),
                        Builders<BsonDocument>.Update
                            .Set(MongoStreamHeadFields.HeadRevision, streamRevision)
                            .Inc(MongoStreamHeadFields.SnapshotRevision, 0)
                            .Inc(MongoStreamHeadFields.Unsnapshotted, eventsCount),
                        UpsertUpdateOptions
                    );
                }
                catch (OutOfMemoryException ex)
                {
                    Logger.LogError(ex, "OutOfMemoryException:");
                    throw;
                }
                catch (Exception ex)
                {
                    //It is safe to ignore transient exception updating stream head.
                    if (Logger.IsEnabled(LogLevel.Warning))
                    {
                        Logger.LogWarning(ex, "Ignored Exception '{exception}' when upserting the stream head Bucket Id [{id}] StreamId[{streamId}].\n", ex.GetType().Name, bucketId, streamId);
                    }
                }
            });
        }

        /// <summary>
        /// Executes the callback within a try/catch block to handle exceptions.
        /// </summary>
        protected virtual T TryMongo<T>(Func<T> callback)
        {
            T? results = default;
#pragma warning disable RCS1021 // Simplify lambda expression.
            TryMongo(() => { results = callback(); }); // do not remove the { } or you'll get recursive calls!
#pragma warning restore RCS1021 // Simplify lambda expression.
            return results!;
        }

        /// <summary>
        /// Executes the callback within a try/catch block to handle exceptions.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="StorageUnavailableException"></exception>
        /// <exception cref="StorageException"></exception>
        protected virtual void TryMongo(Action callback)
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException("Attempt to use storage after it has been disposed.");
            }
            try
            {
                callback();
            }
            catch (MongoConnectionException e)
            {
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning(e, Messages.StorageUnavailable);
                }
                throw new StorageUnavailableException(e.Message, e);
            }
            catch (MongoException e)
            {
                Logger.LogError(e, Messages.StorageThrewException, e.GetType(), e.ToString());
                throw new StorageException(e.Message, e);
            }
        }

        private static BsonDocument GetStreamHeadId(string bucketId, string streamId)
        {
            return new BsonDocument
            {
                [MongoStreamHeadFields.BucketId] = bucketId,
                [MongoStreamHeadFields.StreamId] = streamId
            };
        }

        private Int64 GetLastCommittedCheckPointNumber()
        {
            var filter = Builders<BsonDocument>.Filter.Empty;
            var findOptions = new FindOptions<BsonDocument, BsonDocument>()
            {
                Limit = 1,
                Projection = Builders<BsonDocument>.Projection.Include(MongoCommitFields.CheckpointNumber),
                Sort = Builders<BsonDocument>.Sort.Descending(MongoCommitFields.CheckpointNumber)
            };

            var max = PersistedCommits
               .FindSync(filter, findOptions)
               .FirstOrDefault();

            return max?[MongoCommitFields.CheckpointNumber].AsInt64 ?? 0L;
        }

        /// <summary>
        /// Empties the recycle bin.
        /// </summary>
        public void EmptyRecycleBin()
        {
            var lastCheckpointNumber = GetLastCommittedCheckPointNumber();
            TryMongo(() =>
            {
                PersistedCommits.DeleteMany(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin),
                    Builders<BsonDocument>.Filter.Lt(MongoCommitFields.CheckpointNumber, lastCheckpointNumber)
                ));
            });
        }

        /// <summary>
        /// Gets the commits that have been marked as deleted.
        /// </summary>
        public IEnumerable<ICommit> GetDeletedCommits()
        {
            return TryMongo(() => PersistedCommits
                .Find(Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin))
                .Sort(SortByAscendingCheckpointNumber)
                .ToEnumerable()
                .Select(mc => mc.ToCommit(_serializer)));
        }

        private void CheckIfSnapshotEnabled()
        {
            if (_options.DisableSnapshotSupport)
                throw new NotSupportedException("Snapshot is disabled from MongoPersistenceOptions");
        }

        private void StartBackgroundThread(ThreadStart threadStart)
        {
            if (threadStart != null && _options.PersistStreamHeadsOnBackgroundThread)
            {
                Task.Run(() => threadStart());
            }
            else
            {
                threadStart?.Invoke();
            }
        }

        private void StartBackgroundThread(Func<Task> asyncFunc)
        {
            if (asyncFunc != null && _options.PersistStreamHeadsOnBackgroundThread)
            {
                asyncFunc();
            }
            else
            {
                asyncFunc?.Invoke().ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
    }
}

#pragma warning restore CA2254 // Template should be a static expression
#pragma warning restore IDE0079 // Remove unnecessary suppression
