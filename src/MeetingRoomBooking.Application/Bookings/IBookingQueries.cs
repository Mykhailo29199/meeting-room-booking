namespace MeetingRoomBooking.Application.Bookings;

/// <summary>
/// A booking as shown in lists, with the resource and user details a list
/// needs. <see cref="UserDisplayName"/> and <see cref="UserEmail"/> are null
/// if the account no longer exists.
/// </summary>
public sealed record BookingListItem(
    Guid Id,
    Guid ResourceId,
    string ResourceName,
    string ResourceTimeZoneId,
    string UserId,
    string? UserDisplayName,
    string? UserEmail,
    DateTime StartUtc,
    DateTime EndUtc,
    DateTime CreatedAtUtc);

/// <param name="UserId">Only this user's bookings; null for everyone's.</param>
/// <param name="ResourceId">Only this resource's bookings; null for all resources.</param>
/// <param name="EndsAfterUtc">Only bookings not finished by then; null to include past ones.</param>
/// <param name="Limit">At most this many, earliest first.</param>
public sealed record BookingListFilter(string? UserId, Guid? ResourceId, DateTime? EndsAfterUtc, int Limit);

/// <summary>
/// Read-only booking lists. A separate query rather than a repository method:
/// it joins bookings with resources and user accounts, and user accounts live
/// in Identity, which only Infrastructure knows about.
/// </summary>
public interface IBookingQueries
{
    Task<IReadOnlyList<BookingListItem>> ListAsync(BookingListFilter filter, CancellationToken cancellationToken = default);
}
