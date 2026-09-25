using MeetingRoomBooking.Application.Common;

namespace MeetingRoomBooking.Application.Bookings;

/// <summary>
/// Booking lists: a user's own bookings, and — for admins — everyone's
/// ("view all bookings across users" in the task).
/// </summary>
public sealed class BookingListService
{
    /// <summary>Upper bound on one list, so a request can never pull the whole table.</summary>
    public const int MaxItems = 500;

    private readonly IBookingQueries _queries;
    private readonly TimeProvider _time;

    public BookingListService(IBookingQueries queries, TimeProvider time)
    {
        _queries = queries;
        _time = time;
    }

    /// <summary>The caller's bookings, earliest first; finished ones only if <paramref name="includePast"/>.</summary>
    public Task<IReadOnlyList<BookingListItem>> ListMineAsync(
        UserContext user, bool includePast, CancellationToken cancellationToken = default) =>
        _queries.ListAsync(new BookingListFilter(user.UserId, null, EndsAfter(includePast), MaxItems), cancellationToken);

    /// <summary>
    /// Every user's bookings, optionally for one resource. The API allows this
    /// for admins only.
    /// </summary>
    public Task<IReadOnlyList<BookingListItem>> ListAllAsync(
        Guid? resourceId, bool includePast, CancellationToken cancellationToken = default) =>
        _queries.ListAsync(new BookingListFilter(null, resourceId, EndsAfter(includePast), MaxItems), cancellationToken);

    private DateTime? EndsAfter(bool includePast) => includePast ? null : _time.GetUtcNow().UtcDateTime;
}
