﻿using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace NEventStore.Persistence.MongoDB
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using global::MongoDB.Bson;
    using global::MongoDB.Driver;
    using global::MongoDB.Driver.Builders;
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
        private readonly MongoDatabase _store;
        private readonly MongoCollectionSettings _streamSettings;
        private bool _disposed;
        private int _initialized;
        private readonly Func<LongCheckpoint> _getNextCheckpointNumber;
        private readonly Func<long> _getLastCheckPointNumber;
        private readonly MongoPersistenceOptions _options;
        private readonly WriteConcern _insertCommitWriteConcern;
        private readonly BsonJavaScript _updateScript;
        private readonly LongCheckpoint _checkpointZero;

        public MongoPersistenceEngine(MongoDatabase store, IDocumentSerializer serializer, MongoPersistenceOptions options)
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

            // set config options
            _commitSettings = _options.GetCommitSettings();
            _snapshotSettings = _options.GetSnapshotSettings();
            _streamSettings = _options.GetStreamSettings();
            _insertCommitWriteConcern = _options.GetInsertCommitWriteConcern();

            _getLastCheckPointNumber = () => TryMongo(() =>
            {
                var max = PersistedCommits
                    .FindAll()
                    .SetFields(Fields.Include(MongoCommitFields.CheckpointNumber))
                    .SetSortOrder(SortBy.Descending(MongoCommitFields.CheckpointNumber))
                    .SetLimit(1)
                    .FirstOrDefault();

                return max != null ? max[MongoCommitFields.CheckpointNumber].AsInt64 : 0L;
            });

            _getNextCheckpointNumber = () => new LongCheckpoint(_getLastCheckPointNumber() + 1L);

            _updateScript = new BsonJavaScript("function (x){ return insertCommit(x);}");
            _checkpointZero = new LongCheckpoint(0);
        }

        protected virtual MongoCollection<BsonDocument> PersistedCommits
        {
            get { return _store.GetCollection("Commits", _commitSettings); }
        }

        protected virtual MongoCollection<BsonDocument> PersistedStreamHeads
        {
            get { return _store.GetCollection("Streams", _streamSettings); }
        }

        protected virtual MongoCollection<BsonDocument> PersistedSnapshots
        {
            get { return _store.GetCollection("Snapshots", _snapshotSettings); }
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
                PersistedCommits.CreateIndex(
                    IndexKeys
                        .Ascending(MongoCommitFields.Dispatched)
                        .Ascending(MongoCommitFields.CommitStamp),
                    IndexOptions.SetName(MongoCommitIndexes.Dispatched).SetUnique(false)
                );

                PersistedCommits.CreateIndex(
                    IndexKeys.Ascending(
                            MongoCommitFields.BucketId,
                            MongoCommitFields.StreamId,
                            MongoCommitFields.StreamRevisionFrom,
                            MongoCommitFields.StreamRevisionTo
                    //,MongoCommitFields.FullqualifiedStreamRevision
                    ),
                    IndexOptions.SetName(MongoCommitIndexes.GetFrom).SetUnique(true)
                );

                PersistedCommits.CreateIndex(
                    IndexKeys.Ascending(
                            MongoCommitFields.BucketId,
                            MongoCommitFields.StreamId,
                            MongoCommitFields.CommitSequence
                    ),
                    IndexOptions.SetName(MongoCommitIndexes.LogicalKey).SetUnique(true)
                );

                PersistedCommits.CreateIndex(
                    IndexKeys.Ascending(MongoCommitFields.CommitStamp),
                    IndexOptions.SetName(MongoCommitIndexes.CommitStamp).SetUnique(false)
                );

                PersistedStreamHeads.CreateIndex(
                    IndexKeys.Ascending(MongoStreamHeadFields.Unsnapshotted),
                    IndexOptions.SetName(MongoStreamIndexes.Unsnapshotted).SetUnique(false)
                );

                if (_options.ServerSideOptimisticLoop)
                {
                    PersistedCommits.Database.GetCollection("system.js").Save(new BsonDocument{
                        {"_id" , "insertCommit"},
                        {"value" , new BsonJavaScript(_options.GetInsertCommitScript())}
                    });
                }

                EmptyRecycleBin();
            });
        }

        public virtual IEnumerable<ICommit> GetFrom(string bucketId, string streamId, int minRevision, int maxRevision)
        {
            Logger.Debug(Messages.GettingAllCommitsBetween, streamId, bucketId, minRevision, maxRevision);

            return TryMongo(() =>
            {
                IMongoQuery query = Query.And(
                    Query.EQ(MongoCommitFields.BucketId, bucketId),
                    Query.EQ(MongoCommitFields.StreamId, streamId),
                    Query.LTE(MongoCommitFields.StreamRevisionFrom, maxRevision),
                    Query.GTE(MongoCommitFields.StreamRevisionTo, minRevision));

                return PersistedCommits
                    .Find(query)
                    .SetSortOrder(MongoCommitFields.StreamRevisionFrom)
                    .Select(mc => mc.ToCommit(_serializer));
            });
        }

        public virtual IEnumerable<ICommit> GetFrom(string bucketId, DateTime start)
        {
            Logger.Debug(Messages.GettingAllCommitsFrom, start, bucketId);

            return TryMongo(() => PersistedCommits
                .Find(
                    Query.And(
                        Query.EQ(MongoCommitFields.BucketId, bucketId),
                        Query.GTE(MongoCommitFields.CommitStamp, start)
                    )
                )
                .SetSortOrder(MongoCommitFields.CheckpointNumber)
                .Select(x => x.ToCommit(_serializer)));
        }

        public IEnumerable<ICommit> GetFrom(string bucketId, string checkpointToken)
        {
            var intCheckpoint = LongCheckpoint.Parse(checkpointToken);
            Logger.Debug(Messages.GettingAllCommitsFromBucketAndCheckpoint, bucketId, intCheckpoint.Value);

            return TryMongo(() => PersistedCommits
                .Find(
                    Query.And(
                        Query.EQ(MongoCommitFields.BucketId, bucketId),
                        Query.GT(MongoCommitFields.CheckpointNumber, intCheckpoint.LongValue)
                    )
                )
                .SetSortOrder(MongoCommitFields.CheckpointNumber)
                .Select(x => x.ToCommit(_serializer))
            );
        }

        public IEnumerable<ICommit> GetFrom(string checkpointToken)
        {
            var intCheckpoint = LongCheckpoint.Parse(checkpointToken);
            Logger.Debug(Messages.GettingAllCommitsFromCheckpoint, intCheckpoint.Value);

            return TryMongo(() => PersistedCommits
                .Find(
                    Query.And(
                        Query.NE(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin),
                        Query.GT(MongoCommitFields.CheckpointNumber, intCheckpoint.LongValue)
                    )
                )
                .SetSortOrder(MongoCommitFields.CheckpointNumber)
                .Select(x => x.ToCommit(_serializer))
            );
        }

        public ICheckpoint GetCheckpoint(string checkpointToken = null)
        {
            return LongCheckpoint.Parse(checkpointToken);
        }

        public virtual IEnumerable<ICommit> GetFromTo(string bucketId, DateTime start, DateTime end)
        {
            Logger.Debug(Messages.GettingAllCommitsFromTo, start, end, bucketId);

            return TryMongo(() => PersistedCommits
                .Find(Query.And(
                    Query.EQ(MongoCommitFields.BucketId, bucketId),
                    Query.GTE(MongoCommitFields.CommitStamp, start),
                    Query.LT(MongoCommitFields.CommitStamp, end))
                )
                .SetSortOrder(MongoCommitFields.CheckpointNumber)
                .Select(x => x.ToCommit(_serializer)));
        }

        public virtual ICommit Commit(CommitAttempt attempt)
        {
            Logger.Debug(Messages.AttemptingToCommit, attempt.Events.Count, attempt.StreamId, attempt.CommitSequence);

            return _options.ServerSideOptimisticLoop ?
                PersistWithServerSideOptimisticLoop(attempt) :
                PersistWithClientSideOptimisticLoop(attempt);
        }

        private ICommit PersistWithServerSideOptimisticLoop(CommitAttempt attempt)
        {
            BsonDocument commitDoc = attempt.ToMongoCommit(_checkpointZero, _serializer);

            return TryMongo(() =>
            {
                var result = PersistedCommits.Database.Eval(new EvalArgs()
                {
                    Code = _updateScript,
                    Lock = false,
                    Args = new BsonValue[] { commitDoc }
                });

                if (!result.IsBsonDocument)
                    throw new Exception("Invalid response. Check server side js");

                if (result.AsBsonDocument.Contains("id"))
                {
                    commitDoc["_id"] = result["id"];
                    UpdateStreamHeadAsync(attempt.BucketId, attempt.StreamId, attempt.StreamRevision, attempt.Events.Count);
                    Logger.Debug(Messages.CommitPersisted, attempt.CommitId);
                }
                else if (result.AsBsonDocument.Contains("err"))
                {
                    var errorDocument = result.AsBsonDocument;

                    if (errorDocument["code"] != ConcurrencyExceptionCode)
                    {
                        throw new Exception(errorDocument["err"].AsString);
                    }

                    ICommit savedCommit = PersistedCommits.FindOne(attempt.ToMongoCommitIdQuery()).ToCommit(_serializer);

                    if (savedCommit.CommitId == attempt.CommitId)
                    {
                        throw new DuplicateCommitException(String.Format(
                            "Duplicated Commit: bucketId [{0}]: commitId [{1}] - streamId [{2}] - streamRevision [{3}]",
                             attempt.BucketId, attempt.CommitId, attempt.StreamId, attempt.StreamRevision));
                    }
                    Logger.Debug(Messages.ConcurrentWriteDetected);
                    throw new ConcurrencyException(String.Format(
                        "Concurrency exception forbucketId [{0}]: commitId [{1}] - streamId [{2}] - streamRevision [{3}]\n Inner provider error: {4}",
                        attempt.BucketId, attempt.CommitId, attempt.StreamId, attempt.StreamRevision, errorDocument["err"].AsString));
                }
                else
                {
                    throw new Exception("Invalid response. Check server side js");
                }

                return commitDoc.ToCommit(_serializer);
            });
        }


        private ICommit PersistWithClientSideOptimisticLoop(CommitAttempt attempt)
        {
            return TryMongo(() =>
            {
                BsonDocument commitDoc = attempt.ToMongoCommit(
                    _getNextCheckpointNumber(),
                    _serializer
                );

                bool retry = true;
                while (retry)
                {
                    try
                    {
                        // for concurrency / duplicate commit detection safe mode is required
                        PersistedCommits.Insert(commitDoc, _insertCommitWriteConcern);

                        retry = false;
                        UpdateStreamHeadAsync(attempt.BucketId, attempt.StreamId, attempt.StreamRevision, attempt.Events.Count);
                        Logger.Debug(Messages.CommitPersisted, attempt.CommitId);
                    }
                    catch (MongoException e)
                    {
                        if (!e.Message.Contains(ConcurrencyException))
                        {
                            throw;
                        }

                        // checkpoint index? 
                        if (e.Message.Contains(MongoCommitIndexes.CheckpointNumber))
                        {
                            commitDoc[MongoCommitFields.CheckpointNumber] = _getNextCheckpointNumber().LongValue;
                        }
                        else
                        {
                            ICommit savedCommit = PersistedCommits.FindOne(attempt.ToMongoCommitIdQuery()).ToCommit(_serializer);

                            if (savedCommit.CommitId == attempt.CommitId)
                            {
                                throw new DuplicateCommitException();
                            }
                            Logger.Debug(Messages.ConcurrentWriteDetected);
                            throw new ConcurrencyException();
                        }
                    }
                }

                return commitDoc.ToCommit(_serializer);
            });
        }

        public virtual IEnumerable<ICommit> GetUndispatchedCommits()
        {
            Logger.Debug(Messages.GettingUndispatchedCommits);

            return TryMongo(() => PersistedCommits
                    .Find(Query.EQ("Dispatched", false))
                    .SetSortOrder(MongoCommitFields.CheckpointNumber)
                    .Select(mc => mc.ToCommit(_serializer)));
        }

        public virtual void MarkCommitAsDispatched(ICommit commit)
        {
            Logger.Debug(Messages.MarkingCommitAsDispatched, commit.CommitId);

            TryMongo(() =>
            {
                IMongoQuery query = commit.ToMongoCommitIdQuery();
                UpdateBuilder update = Update.Set(MongoCommitFields.Dispatched, true);
                PersistedCommits.Update(query, update);
            });
        }

        public virtual IEnumerable<IStreamHead> GetStreamsToSnapshot(string bucketId, int maxThreshold)
        {
            Logger.Debug(Messages.GettingStreamsToSnapshot);

            return TryMongo(() =>
            {
                IMongoQuery query = Query.GTE(MongoStreamHeadFields.Unsnapshotted, maxThreshold);
                return PersistedStreamHeads
                    .Find(query)
                    .SetSortOrder(SortBy.Descending(MongoStreamHeadFields.Unsnapshotted))
                    .Select(x => x.ToStreamHead());
            });
        }

        public virtual ISnapshot GetSnapshot(string bucketId, string streamId, int maxRevision)
        {
            Logger.Debug(Messages.GettingRevision, streamId, maxRevision);

            return TryMongo(() => PersistedSnapshots
                .Find(ExtensionMethods.GetSnapshotQuery(bucketId, streamId, maxRevision))
                .SetSortOrder(SortBy.Descending(MongoShapshotFields.Id))
                .SetLimit(1)
                .Select(mc => mc.ToSnapshot(_serializer))
                .FirstOrDefault());
        }

        public virtual bool AddSnapshot(ISnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }
            Logger.Debug(Messages.AddingSnapshot, snapshot.StreamId, snapshot.BucketId, snapshot.StreamRevision);
            try
            {
                BsonDocument mongoSnapshot = snapshot.ToMongoSnapshot(_serializer);
                IMongoQuery query = Query.EQ(MongoShapshotFields.Id, mongoSnapshot[MongoShapshotFields.Id]);
                UpdateBuilder update = Update.Set(MongoShapshotFields.Payload, mongoSnapshot[MongoShapshotFields.Payload]);

                // Doing an upsert instead of an insert allows us to overwrite an existing snapshot and not get stuck with a
                // stream that needs to be snapshotted because the insert fails and the SnapshotRevision isn't being updated.
                PersistedSnapshots.Update(query, update, UpdateFlags.Upsert);

                // More commits could have been made between us deciding that a snapshot is required and writing it so just
                // resetting the Unsnapshotted count may be a little off. Adding snapshots should be a separate process so
                // this is a good chance to make sure the numbers are still in-sync - it only adds a 'read' after all ...
                BsonDocument streamHeadId = GetStreamHeadId(snapshot.BucketId, snapshot.StreamId);
                StreamHead streamHead = PersistedStreamHeads.FindOneById(streamHeadId).ToStreamHead();
                int unsnapshotted = streamHead.HeadRevision - snapshot.StreamRevision;
                PersistedStreamHeads.Update(
                    Query.EQ(MongoStreamHeadFields.Id, streamHeadId),
                    Update.Set(MongoStreamHeadFields.SnapshotRevision, snapshot.StreamRevision).Set(MongoStreamHeadFields.Unsnapshotted, unsnapshotted));

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
            PersistedCommits.RemoveAll();
            PersistedStreamHeads.RemoveAll();
            PersistedSnapshots.RemoveAll();
        }

        public void Purge(string bucketId)
        {
            Logger.Warn(Messages.PurgingBucket, bucketId);
            TryMongo(() =>
            {
                PersistedStreamHeads.Remove(Query.EQ(MongoStreamHeadFields.FullQualifiedBucketId, bucketId));
                PersistedSnapshots.Remove(Query.EQ(MongoShapshotFields.FullQualifiedBucketId, bucketId));
                PersistedCommits.Remove(Query.EQ(MongoCommitFields.BucketId, bucketId));
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
                PersistedStreamHeads.Remove(
                    Query.EQ(MongoStreamHeadFields.Id, new BsonDocument{   
                        {MongoStreamHeadFields.BucketId, bucketId},
                        {MongoStreamHeadFields.StreamId, streamId}
                    })
                );

                PersistedSnapshots.Remove(
                    Query.EQ(MongoShapshotFields.Id, new BsonDocument{   
                        {MongoShapshotFields.BucketId, bucketId},
                        {MongoShapshotFields.StreamId, streamId}
                    })
                );

                PersistedCommits.Update(
                    Query.And(
                        Query.EQ(MongoCommitFields.BucketId, bucketId),
                        Query.EQ(MongoCommitFields.StreamId, streamId)
                    ),
                    Update.Set(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin),
                    UpdateFlags.Multi
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

        private void UpdateStreamHead(string bucketId, string streamId, int streamRevision, int eventsCount)
        {
            const int MaxAttempts = 2;

            bool retry = true;
            var attempt = 0;

            TryMongo(() =>
            {
                while (retry)
                {
                    attempt++;

                    try
                    {
                        BsonDocument streamHeadId = GetStreamHeadId(bucketId, streamId);
                        PersistedStreamHeads.Update(
                            Query.EQ(MongoStreamHeadFields.Id, streamHeadId),
                            Update
                                .Set(MongoStreamHeadFields.HeadRevision, streamRevision)
                                .Inc(MongoStreamHeadFields.SnapshotRevision, 0)
                                .Inc(MongoStreamHeadFields.Unsnapshotted, eventsCount),
                            UpdateFlags.Upsert);

                        retry = false;
                    }
                    catch (MongoDuplicateKeyException ex)
                    {
                        Logger.Warn("Duplicate key exception {0} when upserting the stream head {1} {2}.", ex, bucketId, streamId);
                        
                        retry = attempt > MaxAttempts;
                    }
                }
            });
        }

        private void UpdateStreamHeadAsync(string bucketId, string streamId, int streamRevision, int eventsCount)
        {
            Task.Factory.StartNew(() => UpdateStreamHead(bucketId, streamId, streamRevision, eventsCount));
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
                Logger.Error(Messages.StorageThrewException, e.GetType());
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

        public void EmptyRecycleBin()
        {
            var lastCheckpointNumber = _getLastCheckPointNumber();
            TryMongo(() =>
            {
                PersistedCommits.Remove(Query.And(
                    Query.EQ(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin),
                    Query.LT(MongoCommitFields.CheckpointNumber, lastCheckpointNumber)
                ));
            });
        }

        public IEnumerable<ICommit> GetDeletedCommits()
        {
            return TryMongo(() => PersistedCommits
                                      .Find(Query.EQ(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin))
                                      .SetSortOrder(MongoCommitFields.CheckpointNumber)
                                      .Select(mc => mc.ToCommit(_serializer)));
        }
    }
}