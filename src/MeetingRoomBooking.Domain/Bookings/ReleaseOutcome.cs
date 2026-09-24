namespace MeetingRoomBooking.Domain.Bookings;

/// <summary>What <see cref="Booking.Release"/> did to the booking.</summary>
public enum ReleaseOutcome
{
    /// <summary>The booking had not started: all of it was released and it should be deleted.</summary>
    Cancelled,

    /// <summary>The booking was under way: its future slots were released and it now ends earlier.</summary>
    Shortened
}
