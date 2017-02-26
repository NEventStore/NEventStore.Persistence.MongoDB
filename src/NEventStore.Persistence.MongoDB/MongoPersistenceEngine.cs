using NEventStore.Persistence.MongoDB.Support;

namespace NEventStore.Persistence.MongoDB
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using global::MongoDB.Bson;
    using global::MongoDB.Driver;
    using NEventStore.Logging;
    using NEventStore.Serialization;

    public class MongoPersistenceEngine : IPersistStreams
    {
        private const string ConcurrencyException = "E1100";
        private const int ConcurrencyExceptionCode = 11000;
        private static readonly ILog Logger = LogFactory.BuildLogger(typeof(MongoPersistenceEngine));
        private readonly MongoCollectionSettings _commitSettings;
        private readonly IDocumentSerializer _serializer;
        private readonly MongoCollectionSettings _snapshotSettings;
        private readonly IMongoDatabase _store;
        private readonly MongoCollectionSettings _streamSettings;
        private bool _disposed;
        private int _initialized;

        private readonly MongoPersistenceOptions _options;
        private readonly WriteConcern _insertCommitWriteConcern;

        private readonly string _systemBucketName;

        private ICheckpointGenerator _checkpointGenerator;
        public MongoPersistenceEngine(
            IMongoDatabase store,
            IDocumentSerializer serializer,
            MongoPersistenceOptions options)
        {
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            if (serializer == null)
            {
                throw new ArgumentNullException("serializer");
            }

            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            _store = store;
            _serializer = serializer;
            _options = options;
            _systemBucketName = options.SystemBucketName;

            // set config options
            _commitSettings = _options.GetCommitSettings();
            _snapshotSettings = _options.GetSnapshotSettings();
            _streamSettings = _options.GetStreamSettings();
            _insertCommitWriteConcern = _options.GetInsertCommitWriteConcern();
        }

        protected virtual IMongoCollection<BsonDocument> PersistedCommits
        {
            get { return _store.GetCollection<BsonDocument>("Commits", _commitSettings).WithWriteConcern(_insertCommitWriteConcern); }
        }

        protected virtual IMongoCollection<BsonDocument> PersistedStreamHeads
        {
            get { return _store.GetCollection<BsonDocument>("Streams", _streamSettings); }
        }

        protected virtual IMongoCollection<BsonDocument> PersistedSnapshots
        {
            get { return _store.GetCollection<BsonDocument>("Snapshots", _snapshotSettings); }
        }

        public void Dispose()
        {

            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public virtual void Initialize()
        {
            if (Interlocked.Increment(ref _initialized) > 1)
            {
                return;
            }

            Logger.Debug(Messages.InitializingStorage);

            TryMongo(() =>
            {
                PersistedCommits.Indexes.CreateOne(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(MongoCommitFields.Dispatched)
                        .Ascending(MongoCommitFields.CommitStamp),
                    new CreateIndexOptions()
                    {
                        Name = MongoCommitIndexes.Dispatched,
                        Unique = false
                    }
                );

                PersistedCommits.Indexes.CreateOne(
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
                );

                PersistedCommits.Indexes.CreateOne(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(MongoCommitFields.BucketId)
                        .Ascending(MongoCommitFields.StreamId)
                        .Ascending(MongoCommitFields.CommitSequence),
                    new CreateIndexOptions()
                    {
                        Name = MongoCommitIndexes.LogicalKey,
                        Unique = true
                    }
                );

                PersistedCommits.Indexes.CreateOne(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending(MongoCommitFields.CommitStamp),
                    new CreateIndexOptions()
                    {
                        Name = MongoCommitIndexes.CommitStamp,
                        Unique = false
                    }
                );

                PersistedCommits.Indexes.CreateOne(
                   Builders<BsonDocument>.IndexKeys
                       .Ascending(MongoCommitFields.BucketId)
                       .Ascending(MongoCommitFields.StreamId)
                       .Ascending(MongoCommitFields.CommitId),
                   new CreateIndexOptions()
                   {
                       Name = MongoCommitIndexes.CommitId,
                       Unique = true
                   }
                );

                if (_options.DisableSnapshotSupport == false)
                {
                    PersistedStreamHeads.Indexes.CreateOne(
                        Builders<BsonDocument>.IndexKeys
                            .Ascending(MongoStreamHeadFields.Unsnapshotted),
                        new CreateIndexOptions()
                        {
                            Name = MongoStreamIndexes.Unsnapshotted,
                            Unique = false
                        }
                    );
                }

                _checkpointGenerator = _options.CheckpointGenerator ??
                    new AlwaysQueryDbForNextValueCheckpointGenerator(PersistedCommits);

                EmptyRecycleBin();
            });
        }

        public virtual IEnumerable<ICommit> GetFrom(string bucketId, string streamId, int minRevision, int maxRevision)
        {
            Logger.Debug(Messages.GettingAllCommitsBetween, streamId, bucketId, minRevision, maxRevision);

            return TryMongo(() =>
            {
                var query = Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId),
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.StreamId, streamId),
                    Builders<BsonDocument>.Filter.Lte(MongoCommitFields.StreamRevisionFrom, maxRevision),
                    Builders<BsonDocument>.Filter.Gte(MongoCommitFields.StreamRevisionTo, minRevision)
                );

                // @@review -> sort by commit id?
                return PersistedCommits
                    .Find(query).Sort(Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.StreamRevisionFrom))
                    .ToEnumerable()
                    .Select(mc => mc.ToCommit(_serializer));
            });
        }

        public virtual IEnumerable<ICommit> GetFrom(string bucketId, DateTime start)
        {
            Logger.Debug(Messages.GettingAllCommitsFrom, start, bucketId);

            return TryMongo(() => PersistedCommits
                .Find(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId),
                        Builders<BsonDocument>.Filter.Gte(MongoCommitFields.CommitStamp, start)
                    )
                )
                .Sort(Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.CheckpointNumber))
                .ToEnumerable()
                .Select(x => x.ToCommit(_serializer)));
        }

        public IEnumerable<ICommit> GetFrom(string bucketId, Int64 checkpointToken)
        {

            Logger.Debug(Messages.GettingAllCommitsFromBucketAndCheckpoint, bucketId, checkpointToken);

            return TryMongo(() => PersistedCommits
                .Find(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId),
                        Builders<BsonDocument>.Filter.Gt(MongoCommitFields.CheckpointNumber, checkpointToken)
                    )
                )
                .Sort(Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.CheckpointNumber))
                .ToEnumerable()
                .Select(x => x.ToCommit(_serializer))
            );
        }

        public IEnumerable<ICommit> GetFrom(Int64 checkpointToken)
        {
            Logger.Debug(Messages.GettingAllCommitsFromCheckpoint, checkpointToken);

            return TryMongo(() => PersistedCommits
                .Find(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Ne(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin),
                        Builders<BsonDocument>.Filter.Gt(MongoCommitFields.CheckpointNumber, checkpointToken)
                    )
                )
                .Sort(Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.CheckpointNumber))
                .ToEnumerable()
                .Select(x => x.ToCommit(_serializer))
            );
        }

        public virtual IEnumerable<ICommit> GetFromTo(string bucketId, DateTime start, DateTime end)
        {
            Logger.Debug(Messages.GettingAllCommitsFromTo, start, end, bucketId);

            return TryMongo(() => PersistedCommits
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId),
                    Builders<BsonDocument>.Filter.Gte(MongoCommitFields.CommitStamp, start),
                    Builders<BsonDocument>.Filter.Lt(MongoCommitFields.CommitStamp, end))
                )
                .Sort(Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.CheckpointNumber))
                .ToEnumerable()
                .Select(x => x.ToCommit(_serializer)));
        }

        public virtual ICommit Commit(CommitAttempt attempt)
        {
            Logger.Debug(Messages.AttemptingToCommit, attempt.Events.Count, attempt.StreamId, attempt.CommitSequence);

            return TryMongo(() =>
            {
                Int64 checkpointId;
                var commitDoc = attempt.ToMongoCommit(
                    checkpointId = _checkpointGenerator.Next(),
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
                            UpdateStreamHeadAsync(attempt.BucketId, attempt.StreamId, attempt.StreamRevision, attempt.Events.Count);
                        }
                        Logger.Debug(Messages.CommitPersisted, attempt.CommitId);
                    }
                    catch (MongoException e)
                    {
                        if (!e.Message.Contains(ConcurrencyException))
                        {
                            Logger.Error(Messages.GenericPersistingError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId, e);
                            throw;
                        }

                        // checkpoint index? 
                        if (e.Message.Contains(MongoCommitIndexes.CheckpointNumberMMApV1) ||
                            e.Message.Contains(MongoCommitIndexes.CheckpointNumberWiredTiger))
                        {
                            Logger.Warn(Messages.DuplicatedCheckpointTokenError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId);
                            _checkpointGenerator.SignalDuplicateId(checkpointId);
                            commitDoc[MongoCommitFields.CheckpointNumber] = checkpointId = _checkpointGenerator.Next();
                        }
                        else
                        {
                            if (_options.ConcurrencyStrategy == ConcurrencyExceptionStrategy.FillHole)
                            {
                                FillHole(attempt, checkpointId);
                            }

                            if (e.Message.Contains(MongoCommitIndexes.CommitId))
                            {
                                Logger.Info(Messages.DuplicatedCommitError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId);
                                throw new DuplicateCommitException();
                            }

                            ICommit savedCommit = PersistedCommits
                                .FindSync(attempt.ToMongoCommitIdQuery())
                                .FirstOrDefault()
                                .ToCommit(_serializer);

                            if (savedCommit != null && savedCommit.CommitId == attempt.CommitId)
                            {
                                Logger.Info(Messages.DuplicatedCommitError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId);
                                throw new DuplicateCommitException();
                            }

                            Logger.Info(Messages.ConcurrencyExceptionError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId, e);
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
                   _serializer,
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
                Logger.Warn(Messages.FillHoleError, attempt.CommitId, checkpointId, attempt.BucketId, attempt.StreamId, e);
            }

        }

        public virtual IEnumerable<IStreamHead> GetStreamsToSnapshot(string bucketId, int maxThreshold)
        {
            CheckIfSnapshotEnabled();
            Logger.Debug(Messages.GettingStreamsToSnapshot);

            return TryMongo(() =>
            {
                var query = Builders<BsonDocument>.Filter.Gte(MongoStreamHeadFields.Unsnapshotted, maxThreshold);
                return PersistedStreamHeads
                    .Find(query)
                    .Sort(Builders<BsonDocument>.Sort.Descending(MongoStreamHeadFields.Unsnapshotted))
                    .ToEnumerable()
                    .Select(x => x.ToStreamHead());
            });
        }

        public virtual ISnapshot GetSnapshot(string bucketId, string streamId, int maxRevision)
        {
            CheckIfSnapshotEnabled();

            Logger.Debug(Messages.GettingRevision, streamId, maxRevision);
            var query = ExtensionMethods.GetSnapshotQuery(bucketId, streamId, maxRevision);

            return TryMongo(() => PersistedSnapshots
                .Find(query)
                .Sort(Builders<BsonDocument>.Sort.Descending(MongoShapshotFields.Id))
                .Limit(1)
                .ToEnumerable()
                .Select(mc => mc.ToSnapshot(_serializer))
                .FirstOrDefault());
        }

        public virtual bool AddSnapshot(ISnapshot snapshot)
        {
            CheckIfSnapshotEnabled();

            if (snapshot == null)
            {
                return false;
            }
            Logger.Debug(Messages.AddingSnapshot, snapshot.StreamId, snapshot.BucketId, snapshot.StreamRevision);
            try
            {
                BsonDocument mongoSnapshot = snapshot.ToMongoSnapshot(_serializer);
                var query = Builders<BsonDocument>.Filter.Eq(MongoShapshotFields.Id, mongoSnapshot[MongoShapshotFields.Id]);
                var update = Builders<BsonDocument>.Update.Set(MongoShapshotFields.Payload, mongoSnapshot[MongoShapshotFields.Payload]);

                // Doing an upsert instead of an insert allows us to overwrite an existing snapshot and not get stuck with a
                // stream that needs to be snapshotted because the insert fails and the SnapshotRevision isn't being updated.
                PersistedSnapshots.UpdateOne(query, update, new UpdateOptions() { IsUpsert = true });

                // More commits could have been made between us deciding that a snapshot is required and writing it so just
                // resetting the Unsnapshotted count may be a little off. Adding snapshots should be a separate process so
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
            catch (Exception)
            {
                return false;
            }
        }

        public virtual void Purge()
        {
            Logger.Warn(Messages.PurgingStorage);
            // @@review -> drop & create?
            PersistedCommits.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            PersistedStreamHeads.DeleteMany(Builders<BsonDocument>.Filter.Empty);
            PersistedSnapshots.DeleteMany(Builders<BsonDocument>.Filter.Empty);
        }

        public void Purge(string bucketId)
        {
            Logger.Warn(Messages.PurgingBucket, bucketId);
            TryMongo(() =>
            {
                PersistedStreamHeads.DeleteMany(Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.FullQualifiedBucketId, bucketId));
                PersistedSnapshots.DeleteMany(Builders<BsonDocument>.Filter.Eq(MongoShapshotFields.FullQualifiedBucketId, bucketId));
                PersistedCommits.DeleteMany(Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId));
            });

        }

        public void Drop()
        {
            Purge();
        }

        public void DeleteStream(string bucketId, string streamId)
        {
            Logger.Warn(Messages.DeletingStream, streamId, bucketId);
            TryMongo(() =>
            {
                PersistedStreamHeads.DeleteOne(
                    Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.Id, new BsonDocument{
                        {MongoStreamHeadFields.BucketId, bucketId},
                        {MongoStreamHeadFields.StreamId, streamId}
                    })
                );

                PersistedSnapshots.DeleteMany(
                    Builders<BsonDocument>.Filter.Eq(MongoShapshotFields.Id, new BsonDocument{
                        {MongoShapshotFields.BucketId, bucketId},
                        {MongoShapshotFields.StreamId, streamId}
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

        public bool IsDisposed
        {
            get { return _disposed; }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing || _disposed)
            {
                return;
            }

            Logger.Debug(Messages.ShuttingDownPersistence);
            _disposed = true;
        }

        private void UpdateStreamHeadAsync(string bucketId, string streamId, int streamRevision, int eventsCount)
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
                        new UpdateOptions() { IsUpsert = true }
                    );
                }
                catch (OutOfMemoryException ex)
                {
					Logger.Error("OutOfMemoryException: {0}", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    //It is safe to ignore transient exception updating stream head.
                    Logger.Warn("Ignored Exception '{0}' when upserting the stream head Bucket Id [{1}] StreamId[{2}].\n {3}", ex.GetType().Name, bucketId, streamId, ex.ToString());
                }
            });
        }



        protected virtual T TryMongo<T>(Func<T> callback)
        {
            T results = default(T);
            TryMongo(() => { results = callback(); });
            return results;
        }

        protected virtual void TryMongo(Action callback)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("Attempt to use storage after it has been disposed.");
            }
            try
            {
                callback();
            }
            catch (MongoConnectionException e)
            {
                Logger.Warn(Messages.StorageUnavailable);
                throw new StorageUnavailableException(e.Message, e);
            }
            catch (MongoException e)
            {
                Logger.Error(Messages.StorageThrewException, e.GetType(), e.ToString());
                throw new StorageException(e.Message, e);
            }
        }

        private static BsonDocument GetStreamHeadId(string bucketId, string streamId)
        {
            var id = new BsonDocument();
            id[MongoStreamHeadFields.BucketId] = bucketId;
            id[MongoStreamHeadFields.StreamId] = streamId;
            return id;
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

            return max != null ? max[MongoCommitFields.CheckpointNumber].AsInt64 : 0L;
        }

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

        public IEnumerable<ICommit> GetDeletedCommits()
        {
            return TryMongo(() => PersistedCommits
                                      .Find(Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin))
                                      .Sort(Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.CheckpointNumber))
                                      .ToEnumerable()
                                      .Select(mc => mc.ToCommit(_serializer)));
        }

        private void CheckIfSnapshotEnabled()
        {
            if (_options.DisableSnapshotSupport)
                throw new NotSupportedException("Snapshot is disabled from MongoPersistenceOptions");
        }

		private static void StartBackgroundThread(ThreadStart threadStart)
		{
			if (threadStart != null)
			{
				var thread = new Thread(threadStart);
				thread.IsBackground = true;
				thread.Start();
			}
		}
    }
}