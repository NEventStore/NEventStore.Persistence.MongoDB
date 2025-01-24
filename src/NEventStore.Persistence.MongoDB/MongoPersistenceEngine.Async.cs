#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA2254 // Template should be a static expression

using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Runtime.ExceptionServices;

namespace NEventStore.Persistence.MongoDB
{
    public partial class MongoPersistenceEngine : IPersistStreams
    {
        /// <inheritdoc/>
        public Task GetFromAsync(string bucketId, string streamId, int minRevision, int maxRevision, IAsyncObserver<ICommit> observer, CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingAllCommitsBetween, streamId, bucketId, minRevision, maxRevision);
            }

            return TryMongoAsync(async () =>
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

                using var cursor = await PersistedCommits
                    .Find(query)
                    // .Sort(Builders<BsonDocument>.Sort.Ascending(MongoCommitFields.StreamRevisionFrom))
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToCursorAsync(cancellationToken)
                    .ConfigureAwait(false);

                while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var doc in cursor.Current)
                    {
                        var res = await observer.OnNextAsync(doc.ToCommit(_serializer), cancellationToken).ConfigureAwait(false);
                        if (!res)
                        {
                            await observer.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                await observer.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken, observer);
        }

        /// <inheritdoc/>
        public Task GetFromAsync(string bucketId, long checkpointToken, IAsyncObserver<ICommit> asyncObserver, CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingAllCommitsFromBucketAndCheckpoint, bucketId, checkpointToken);
            }

            return TryMongoAsync(async () =>
            {
                var query = Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId);
                if (checkpointToken > 0)
                {
                    query = Builders<BsonDocument>.Filter.And(
                        query,
                        Builders<BsonDocument>.Filter.Gt(MongoCommitFields.CheckpointNumber, checkpointToken)
                    );
                }
                using var cursor = await PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToCursorAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var doc in cursor.Current)
                    {
                        var res = await asyncObserver.OnNextAsync(doc.ToCommit(_serializer), cancellationToken).ConfigureAwait(false);
                        if (!res)
                        {
                            await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken,
            asyncObserver);
        }

        /// <inheritdoc/>
        public Task GetFromToAsync(string bucketId, long fromCheckpointToken, long toCheckpointToken, IAsyncObserver<ICommit> asyncObserver, CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingCommitsFromBucketAndFromToCheckpoint, bucketId, fromCheckpointToken, toCheckpointToken);
            }

            return TryMongoAsync(async () =>
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
                using var cursor = await PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToCursorAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var doc in cursor.Current)
                    {
                        var res = await asyncObserver.OnNextAsync(doc.ToCommit(_serializer), cancellationToken).ConfigureAwait(false);
                        if (!res)
                        {
                            await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken,
            asyncObserver);
        }

        /// <inheritdoc/>
        public Task GetFromAsync(long checkpointToken, IAsyncObserver<ICommit> asyncObserver, CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingAllCommitsFromCheckpoint, checkpointToken);
            }

            return TryMongoAsync(async () =>
            {
                var query = Builders<BsonDocument>.Filter.Ne(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin);
                if (checkpointToken > 0)
                {
                    query = Builders<BsonDocument>.Filter.And(
                        query,
                        Builders<BsonDocument>.Filter.Gt(MongoCommitFields.CheckpointNumber, checkpointToken)
                    );
                }
                using var cursor = await PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToCursorAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var doc in cursor.Current)
                    {
                        var res = await asyncObserver.OnNextAsync(doc.ToCommit(_serializer), cancellationToken).ConfigureAwait(false);
                        if (!res)
                        {
                            await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken,
            asyncObserver);
        }

        /// <inheritdoc/>
        public Task GetFromToAsync(long fromCheckpointToken, long toCheckpointToken, IAsyncObserver<ICommit> asyncObserver, CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingCommitsFromToCheckpoint, fromCheckpointToken, toCheckpointToken);
            }

            return TryMongoAsync(async () =>
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

                using var cursor = await PersistedCommits
                    .Find(query)
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToCursorAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var doc in cursor.Current)
                    {
                        var res = await asyncObserver.OnNextAsync(doc.ToCommit(_serializer), cancellationToken).ConfigureAwait(false);
                        if (!res)
                        {
                            await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken,
            asyncObserver);
        }

        /// <inheritdoc/>
        public async Task<ICommit?> CommitAsync(CommitAttempt attempt, CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.AttemptingToCommit, attempt.Events.Count, attempt.StreamId, attempt.CommitSequence);
            }

            // async/await was used to avoid Task<ICommit> to Task<ICommit?> conversion issues
            return await TryMongoAsync(async () =>
            {
                Int64 checkpointId = await _checkpointGenerator!.NextAsync(cancellationToken).ConfigureAwait(false);
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
                        await PersistedCommits.InsertOneAsync(commitDoc, options: null, cancellationToken).ConfigureAwait(false);

                        retry = false;
                        if (!_options.DisableSnapshotSupport)
                        {
                            UpdateStreamHeadInBackgroundThread(attempt.BucketId, attempt.StreamId, attempt.StreamRevision, attempt.Events.Count, cancellationToken);
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
                            await _checkpointGenerator.SignalDuplicateIdAsync(checkpointId, cancellationToken);
                            checkpointId = await _checkpointGenerator.NextAsync(cancellationToken).ConfigureAwait(false);
                            commitDoc[MongoCommitFields.CheckpointNumber] = checkpointId;
                        }
                        else
                        {
                            if (_options.ConcurrencyStrategy == ConcurrencyExceptionStrategy.FillHole)
                            {
                                await FillHoleAsync(attempt, checkpointId, cancellationToken).ConfigureAwait(false);
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
                            using var asyncCursor = await PersistedCommits
                                .FindAsync(attempt.ToMongoCommitIdQuery(), cancellationToken: cancellationToken)
                                .ConfigureAwait(false);
                            var bsonSavedCommit = await asyncCursor
                                .FirstOrDefaultAsync()
                                .ConfigureAwait(false);
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
            }).ConfigureAwait(false);
        }

        private async Task FillHoleAsync(CommitAttempt attempt, Int64 checkpointId, CancellationToken cancellationToken)
        {
            try
            {
                var holeFillDoc = attempt.ToEmptyCommit(
                   checkpointId,
                   _systemBucketName
                );
                await PersistedCommits.InsertOneAsync(holeFillDoc, options: null, cancellationToken).ConfigureAwait(false);
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
        public Task GetStreamsToSnapshotAsync(string bucketId, int maxThreshold, IAsyncObserver<IStreamHead> asyncObserver, CancellationToken cancellationToken)
        {
            CheckIfSnapshotEnabled();

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingStreamsToSnapshot);
            }

            return TryMongoAsync(async () =>
            {
                var query = Builders<BsonDocument>.Filter.Gte(MongoStreamHeadFields.Unsnapshotted, maxThreshold);
                using var cursor = await PersistedStreamHeads
                    .Find(query)
                    .Sort(Builders<BsonDocument>.Sort.Descending(MongoStreamHeadFields.Unsnapshotted))
                    .ToCursorAsync(cancellationToken)
                    .ConfigureAwait(false);

                while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var doc in cursor.Current)
                    {
                        var res = await asyncObserver.OnNextAsync(doc.ToStreamHead(), cancellationToken).ConfigureAwait(false);
                        if (!res)
                        {
                            await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken, asyncObserver);
        }

        /// <inheritdoc/>
        public Task<ISnapshot?> GetSnapshotAsync(string bucketId, string streamId, int maxRevision, CancellationToken cancellationToken)
        {
            CheckIfSnapshotEnabled();

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(Messages.GettingRevision, streamId, maxRevision);
            }

            return TryMongoAsync(async () =>
            {
                var query = ExtensionMethods.GetSnapshotQuery(bucketId, streamId, maxRevision);

                using var cursor = await PersistedSnapshots
                    .Find(query)
                    .Sort(Builders<BsonDocument>.Sort.Descending(MongoSnapshotFields.Id))
                    .Limit(1)
                    .ToCursorAsync(cancellationToken)
                    .ConfigureAwait(false);

                var snapshot = await cursor.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return snapshot?.ToSnapshot(_serializer) as ISnapshot;
            });
        }

        /// <inheritdoc/>
        public async Task<bool> AddSnapshotAsync(ISnapshot snapshot, CancellationToken cancellationToken)
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
                await PersistedSnapshots.UpdateOneAsync(query, update, UpsertUpdateOptions, cancellationToken).ConfigureAwait(false);

                // More commits could have been made between us deciding that a snapshot is required and writing it so just
                // resetting the Un-snapshotted count may be a little off. Adding snapshots should be a separate process so
                // this is a good chance to make sure the numbers are still in-sync - it only adds a 'read' after all ...
                BsonDocument streamHeadId = GetStreamHeadId(snapshot.BucketId, snapshot.StreamId);

                using var cursor = await PersistedStreamHeads
                    .FindAsync(Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.Id, streamHeadId), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                StreamHead streamHead = (await cursor.FirstAsync(cancellationToken).ConfigureAwait(false))
                    .ToStreamHead();

                int unsnapshotted = streamHead.HeadRevision - snapshot.StreamRevision;

                await PersistedStreamHeads.UpdateOneAsync(
                    Builders<BsonDocument>.Filter
                        .Eq(MongoStreamHeadFields.Id, streamHeadId),
                    Builders<BsonDocument>.Update
                        .Set(MongoStreamHeadFields.SnapshotRevision, snapshot.StreamRevision)
                        .Set(MongoStreamHeadFields.Unsnapshotted, unsnapshotted),
                    cancellationToken: cancellationToken
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
        public Task PurgeAsync(CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning(Messages.PurgingStorage);
            }
            return TryMongoAsync<object>(async () =>
            {
                await PersistedCommits.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken).ConfigureAwait(false);
                await PersistedStreamHeads.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken).ConfigureAwait(false);
                await PersistedSnapshots.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
        }

        /// <inheritdoc/>
        public Task PurgeAsync(string bucketId, CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning(Messages.PurgingBucket, bucketId);
            }
            return TryMongoAsync<object>(async () =>
            {
                await PersistedStreamHeads.DeleteManyAsync(Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.FullQualifiedBucketId, bucketId), cancellationToken).ConfigureAwait(false);
                await PersistedSnapshots.DeleteManyAsync(Builders<BsonDocument>.Filter.Eq(MongoSnapshotFields.FullQualifiedBucketId, bucketId), cancellationToken).ConfigureAwait(false);
                await PersistedCommits.DeleteManyAsync(Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId), cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
        }

        /// <inheritdoc/>
        public Task DeleteStreamAsync(string bucketId, string streamId, CancellationToken cancellationToken)
        {
            if (Logger.IsEnabled(LogLevel.Warning))
            {
                Logger.LogWarning(Messages.DeletingStream, streamId, bucketId);
            }

            return TryMongoAsync<object>(async () =>
            {
                await PersistedStreamHeads.DeleteOneAsync(
                    Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.Id, new BsonDocument{
                        {MongoStreamHeadFields.BucketId, bucketId},
                        {MongoStreamHeadFields.StreamId, streamId}
                    }),
                    cancellationToken
                ).ConfigureAwait(false);

                await PersistedSnapshots.DeleteManyAsync(
                    Builders<BsonDocument>.Filter.Eq(MongoSnapshotFields.Id, new BsonDocument{
                        {MongoSnapshotFields.BucketId, bucketId},
                        {MongoSnapshotFields.StreamId, streamId}
                    }),
                    cancellationToken
                ).ConfigureAwait(false);

                await PersistedCommits.UpdateManyAsync(
                    Builders<BsonDocument>.Filter.And(
                        Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, bucketId),
                        Builders<BsonDocument>.Filter.Eq(MongoCommitFields.StreamId, streamId)
                    ),
                    Builders<BsonDocument>.Update.Set(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin),
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);
            },
            cancellationToken);
        }

        private void UpdateStreamHeadInBackgroundThread(string bucketId, string streamId, int streamRevision, int eventsCount, CancellationToken cancellationToken)
        {
            StartBackgroundThread(async () =>
            {
                try
                {
                    BsonDocument streamHeadId = GetStreamHeadId(bucketId, streamId);
                    await PersistedStreamHeads.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq(MongoStreamHeadFields.Id, streamHeadId),
                        Builders<BsonDocument>.Update
                            .Set(MongoStreamHeadFields.HeadRevision, streamRevision)
                            .Inc(MongoStreamHeadFields.SnapshotRevision, 0)
                            .Inc(MongoStreamHeadFields.Unsnapshotted, eventsCount),
                        UpsertUpdateOptions,
                        cancellationToken
                    ).ConfigureAwait(false);
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
        protected virtual async Task<T> TryMongoAsync<T>(Func<Task<T>> callbackAsync)
        {
            T? results = default;
#pragma warning disable RCS1021 // Simplify lambda expression.
            await TryMongoAsync<T>(
                async () => { results = await callbackAsync().ConfigureAwait(false); },
                CancellationToken.None,
                asyncObserver: null
                ).ConfigureAwait(false); // do not remove the { } or you'll get recursive calls!
#pragma warning restore RCS1021 // Simplify lambda expression.
            return results!;
        }

        /// <summary>
        /// Executes the callback within a try/catch block to handle exceptions.
        /// If an observer is provided, it will be notified of any exceptions that occur during the operation.
        /// If an observer in provided and the operation was cancelled, the observer will be notified of the cancellation calling OnCompleteAsync.
        /// If no observer is provided, exceptions will be raised
        /// </summary>
        /// <param name="callbackAsync">Callback to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="asyncObserver">
        /// If an observer is provided, it will be notified of any exceptions that occur during the operation.
        /// If no observer is provided, exceptions will be raised
        /// </param>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="StorageUnavailableException"></exception>
        /// <exception cref="StorageException"></exception>
        protected virtual async Task TryMongoAsync<T>(Func<Task> callbackAsync, CancellationToken cancellationToken, IAsyncObserver<T>? asyncObserver = null)
        {
            ExceptionDispatchInfo? exception = null;
            if (IsDisposed)
            {
                throw new ObjectDisposedException("Attempt to use storage after it has been disposed.");
            }
            try
            {
                await callbackAsync().ConfigureAwait(false);
            }
            catch (MongoConnectionException e)
            {
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning(e, Messages.StorageUnavailable);
                }
                exception = ExceptionDispatchInfo.Capture(new StorageUnavailableException(e.Message, e));
            }
            catch (MongoException e)
            {
                Logger.LogError(e, Messages.StorageThrewException, e.GetType(), e.ToString());
                exception = ExceptionDispatchInfo.Capture(new StorageException(e.Message, e));
            }
            catch (TaskCanceledException ex)
            {
                if (Logger.IsEnabled(LogLevel.Warning))
                {
                    Logger.LogWarning(ex, "Task was cancelled.");
                }
                asyncObserver?.OnCompletedAsync(cancellationToken);
            }
            catch (Exception e)
            {
                if (Logger.IsEnabled(LogLevel.Error))
                {
                    Logger.LogError(e, Messages.StorageThrewException, e.GetType(), e.ToString());
                }
                exception = ExceptionDispatchInfo.Capture(e);
            }
            if (exception != null)
            {
                if (asyncObserver != null)
                {
                    await asyncObserver.OnErrorAsync(exception.SourceException, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    exception.Throw();
                }
            }
        }

        private async Task<Int64> GetLastCommittedCheckPointNumberAsync(CancellationToken cancellationToken)
        {
            var filter = Builders<BsonDocument>.Filter.Empty;
            var findOptions = new FindOptions<BsonDocument, BsonDocument>()
            {
                Limit = 1,
                Projection = Builders<BsonDocument>.Projection.Include(MongoCommitFields.CheckpointNumber),
                Sort = Builders<BsonDocument>.Sort.Descending(MongoCommitFields.CheckpointNumber)
            };

            var max = await PersistedCommits
               .FindSync(filter, findOptions, cancellationToken)
               .FirstOrDefaultAsync(cancellationToken)
               .ConfigureAwait(false);

            return max?[MongoCommitFields.CheckpointNumber].AsInt64 ?? 0L;
        }

        /// <summary>
        /// Empties the recycle bin.
        /// </summary>
        public async Task EmptyRecycleBinAsync(CancellationToken cancellationToken)
        {
            var lastCheckpointNumber = await GetLastCommittedCheckPointNumberAsync(cancellationToken).ConfigureAwait(false);
            await TryMongoAsync<object>(() =>
            {
                return PersistedCommits.DeleteManyAsync(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin),
                    Builders<BsonDocument>.Filter.Lt(MongoCommitFields.CheckpointNumber, lastCheckpointNumber)
                ), cancellationToken);
            },
            cancellationToken,
            asyncObserver: null);
        }

        /// <summary>
        /// Gets the commits that have been marked as deleted.
        /// </summary>
        public Task GetDeletedCommitsAsync(IAsyncObserver<ICommit> asyncObserver, CancellationToken cancellationToken)
        {
            return TryMongoAsync(async () =>
            {
                using var cursor = await PersistedCommits
                    .Find(Builders<BsonDocument>.Filter.Eq(MongoCommitFields.BucketId, MongoSystemBuckets.RecycleBin))
                    .Sort(SortByAscendingCheckpointNumber)
                    .ToCursorAsync(cancellationToken)
                    .ConfigureAwait(false);
                while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    foreach (var doc in cursor.Current)
                    {
                        var res = await asyncObserver.OnNextAsync(doc.ToCommit(_serializer), cancellationToken).ConfigureAwait(false);
                        if (!res)
                        {
                            await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }
                await asyncObserver.OnCompletedAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken,
            asyncObserver);
        }
    }
}

#pragma warning restore CA2254 // Template should be a static expression
#pragma warning restore IDE0079 // Remove unnecessary suppression