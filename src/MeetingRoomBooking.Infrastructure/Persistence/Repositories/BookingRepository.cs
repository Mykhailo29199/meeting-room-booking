using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain.Bookings;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Infrastructure.Persistence.Repositories;

internal sealed class BookingRepository : IBookingRepository
{
    private readonly AppDbContext _context;

    public BookingRepository(AppDbContext context) => _context = context;

    /// <summary>Loads the booking with its slots, which releasing it needs.</summary>
    public async Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await _context.Bookings.Include(b => b.Slots).SingleOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Booking>> GetUnfinishedByResourceAsync(
        Guid resourceId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await _context.Bookings
            .Include(b => b.Slots)
            .Where(b => b.ResourceId == resourceId && b.EndUtc > nowUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BookedSlot>> GetBookedSlotsAsync(
        Guid resourceId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        // Range scan on the clustered primary key (ResourceId, SlotStartUtc).
        await (
            from slot in _context.BookingSlots.AsNoTracking()
            join booking in _context.Bookings.AsNoTracking() on slot.BookingId equals booking.Id
            where slot.ResourceId == resourceId && slot.SlotStartUtc >= fromUtc && slot.SlotStartUtc < toUtc
            orderby slot.SlotStartUtc
            select new BookedSlot(slot.SlotStartUtc, booking.Id, booking.UserId)
        ).ToListAsync(cancellationToken);

    /// <summary>Tracks the booking; its slots are inserted with it through the Slots navigation.</summary>
    public void Add(Booking booking) => _context.Bookings.Add(booking);

    /// <summary>The slots are removed by the database's cascade delete (see BookingConfiguration).</summary>
    public void Remove(Booking booking) => _context.Bookings.Remove(booking);
}
