using MeetingRoomBooking.Domain.Bookings;

namespace MeetingRoomBooking.Application.Persistence;

/// <summary>Stages and reads bookings. Nothing is saved until <see cref="IUnitOfWork.CompleteAsync"/>.</summary>
public interface IBookingRepository
{
    /// <summary>
    /// Stages the booking together with its slots. There is deliberately no
    /// "is it free?" check: the database decides at commit time.
    /// </summary>
    void Add(Booking booking);
}
