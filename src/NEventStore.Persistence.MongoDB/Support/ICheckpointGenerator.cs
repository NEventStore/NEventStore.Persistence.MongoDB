namespace NEventStore.Persistence.MongoDB.Support
{
    /// <summary>
    /// Generates a new checkpoint id
    /// </summary>
    public interface ICheckpointGenerator
    {
        /// <summary>
        /// Generates a new checkpoint id
        /// </summary>
        Int64 Next();

        /// <summary>
        /// Generates a new checkpoint id
        /// </summary>
        Task<Int64> NextAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The id generated is not valid, it is duplicated.
        /// We might need to reinitialize the checkpoint generator; iIt is necessary
        /// when there are multiple processes that generates id in autonomous way.
        /// </summary>
        void SignalDuplicateId(Int64 id);

        /// <summary>
        /// The id generated is not valid, it is duplicated.
        /// We might need to reinitialize the checkpoint generator; iIt is necessary
        /// when there are multiple processes that generates id in autonomous way.
        /// </summary>
        Task SignalDuplicateIdAsync(Int64 id, CancellationToken cancellationToken = default);
    }
}
