using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain.Bookings;

namespace MeetingRoomBooking.Infrastructure.Persistence.Repositories;

internal sealed class BookingRepository : IBookingRepository
{
    private readonly AppDbContext _context;

    public BookingRepository(AppDbContext context) => _context = context;

    /// <summary>Tracks the booking; its slots are inserted with it through the Slots navigation.</summary>
    public void Add(Booking booking) => _context.Bookings.Add(booking);
}
