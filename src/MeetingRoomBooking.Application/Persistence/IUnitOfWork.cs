namespace MeetingRoomBooking.Application.Persistence;

/// <summary>
/// One business transaction. Repositories only stage changes;
/// <see cref="CompleteAsync"/> is the single commit point, and the single
/// place database conflicts surface — translated into Application exceptions
/// so services never depend on EF Core or a specific database.
/// </summary>
public interface IUnitOfWork
{
    IResourceRepository Resources { get; }

    IBookingRepository Bookings { get; }

    /// <summary>
    /// Starts an explicit transaction. Until it is committed, every
    /// <see cref="CompleteAsync"/> call and every lock taken belongs to it.
    /// </summary>
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits every staged change atomically: all of it is saved, or none of it.</summary>
    /// <exception cref="UniqueConstraintViolationException">
    /// A row violates a unique constraint — for bookings, one of the slots is already taken.
    /// </exception>
    /// <exception cref="ConcurrencyConflictException">
    /// An edited row was changed by someone else since it was loaded.
    /// </exception>
    Task CompleteAsync(CancellationToken cancellationToken = default);
}
