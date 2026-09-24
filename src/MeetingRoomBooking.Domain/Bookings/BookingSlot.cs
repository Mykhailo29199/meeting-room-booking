namespace MeetingRoomBooking.Domain.Bookings;

/// <summary>
/// One 15-minute slot occupied by a booking. A booking for 10:00–11:00 owns
/// four of these: 10:00, 10:15, 10:30 and 10:45.
///
/// This is the row the concurrency guarantee rests on: the database allows
/// only one slot per (ResourceId, SlotStartUtc). Two bookings that overlap in
/// any way — not just ones with the same start time — share at least one
/// slot, so the database rejects all but one of them.
/// </summary>
public class BookingSlot
{
    public Guid BookingId { get; private set; }
    public Guid ResourceId { get; private set; }
    public DateTime SlotStartUtc { get; private set; }

    // Used by EF Core when loading from the database.
    private BookingSlot() { }

    internal BookingSlot(Guid bookingId, Guid resourceId, DateTime slotStartUtc)
    {
        BookingId = bookingId;
        ResourceId = resourceId;
        SlotStartUtc = slotStartUtc;
    }
}
