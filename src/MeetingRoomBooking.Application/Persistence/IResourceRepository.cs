using MeetingRoomBooking.Domain.Resources;

namespace MeetingRoomBooking.Application.Persistence;

/// <summary>Stages and reads resources. Nothing is saved until <see cref="IUnitOfWork.CompleteAsync"/>.</summary>
public interface IResourceRepository
{
    /// <summary>The resource, tracked for changes; null if it does not exist.</summary>
    Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes an exclusive lock on the resource's row until the current
    /// transaction (<see cref="IUnitOfWork.BeginTransactionAsync"/>) ends, and
    /// returns false if the resource does not exist.
    ///
    /// Booking and removing a resource both take this lock first, so they
    /// never interleave: a booking either commits before the removal starts
    /// (and the removal then releases it) or waits and then sees the resource
    /// as removed. Anything read after the lock is current.
    /// </summary>
    Task<bool> LockAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Resources ordered by name, read-only; inactive (removed) ones only if asked for.</summary>
    Task<IReadOnlyList<Resource>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default);

    void Add(Resource resource);

    /// <summary>The optimistic-concurrency version of a loaded or saved resource.</summary>
    Guid GetVersion(Resource resource);

    /// <summary>
    /// Declares which version an edit is based on (the one the client loaded).
    /// Saving then fails with <see cref="ConcurrencyConflictException"/> if the
    /// stored version differs — someone else changed the resource meanwhile.
    /// </summary>
    void SetExpectedVersion(Resource resource, Guid version);
}
