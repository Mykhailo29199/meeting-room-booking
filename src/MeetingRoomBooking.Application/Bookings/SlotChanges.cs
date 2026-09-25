using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Domain.Bookings;

namespace MeetingRoomBooking.Application.Bookings;

internal static class SlotChanges
{
    /// <summary>The given slots as notifications, in time order.</summary>
    public static IReadOnlyList<SlotChange> Of(IEnumerable<BookingSlot> slots, bool isBooked) =>
        slots
            .OrderBy(s => s.SlotStartUtc)
            .Select(s => new SlotChange(s.SlotStartUtc, s.SlotStartUtc + TimeSlots.Length, isBooked))
            .ToList();
}
