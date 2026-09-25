using MeetingRoomBooking.Application.Bookings;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Infrastructure.Persistence;

/// <summary>
/// <see cref="IBookingQueries"/> as one SQL query: bookings joined with
/// their resource and (left-joined, as there is no foreign key) the user
/// account. Read-only, no change tracking.
/// </summary>
public sealed class BookingQueries : IBookingQueries
{
    private readonly AppDbContext _context;

    public BookingQueries(AppDbContext context) => _context = context;

    public async Task<IReadOnlyList<BookingListItem>> ListAsync(BookingListFilter filter, CancellationToken cancellationToken = default)
    {
        var bookings = _context.Bookings.AsNoTracking();
        if (filter.UserId is not null)
            bookings = bookings.Where(b => b.UserId == filter.UserId);
        if (filter.ResourceId is not null)
            bookings = bookings.Where(b => b.ResourceId == filter.ResourceId);
        if (filter.EndsAfterUtc is not null)
            bookings = bookings.Where(b => b.EndUtc > filter.EndsAfterUtc);

        return await (
            from booking in bookings
            join resource in _context.Resources.AsNoTracking() on booking.ResourceId equals resource.Id
            join user in _context.Users.AsNoTracking() on booking.UserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            orderby booking.StartUtc, booking.Id
            select new BookingListItem(
                booking.Id,
                resource.Id,
                resource.Name,
                resource.TimeZoneId,
                booking.UserId,
                user == null ? null : user.DisplayName,
                user == null ? null : user.Email,
                booking.StartUtc,
                booking.EndUtc,
                booking.CreatedAtUtc)
        ).Take(filter.Limit).ToListAsync(cancellationToken);
    }
}
