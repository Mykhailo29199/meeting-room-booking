namespace MeetingRoomBooking.Application.Persistence;

/// <summary>
/// An explicit database transaction, for use cases whose reads and writes
/// must happen atomically with a lock held in between (see
/// <see cref="IResourceRepository.LockAsync"/>). Disposing it without
/// <see cref="CommitAsync"/> rolls everything back.
/// </summary>
public interface ITransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
