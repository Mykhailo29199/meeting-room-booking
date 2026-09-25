using MeetingRoomBooking.Domain.Bookings;

namespace MeetingRoomBooking.Application.Persistence;

/// <summary>Stages and reads bookings. Nothing is saved until <see cref="IUnitOfWork.CompleteAsync"/>.</summary>
public interface IBookingRepository
{
    /// <summary>The booking with its slots, tracked for changes; null if it does not exist.</summary>
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bookings of <paramref name="resourceId"/> that have not ended by
    /// <paramref name="nowUtc"/>, with their slots, tracked for changes.
    /// </summary>
    Task<IReadOnlyList<Booking>> GetUnfinishedByResourceAsync(
        Guid resourceId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Taken slots of <paramref name="resourceId"/> starting in
    /// [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), in time order. Read-only.
    /// </summary>
    Task<IReadOnlyList<BookedSlot>> GetBookedSlotsAsync(
        Guid resourceId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the booking together with its slots. There is deliberately no
    /// "is it free?" check: the database decides at commit time.
    /// </summary>
    void Add(Booking booking);

    /// <summary>Stages deletion of the booking; the database deletes its slots with it, freeing them.</summary>
    void Remove(Booking booking);
}
