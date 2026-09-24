namespace MeetingRoomBooking.Domain;

/// <summary>
/// The booking grid: resources are booked in whole 15-minute slots that start
/// on the quarter hour (10:00, 10:15, 10:30, ...). A booking can be one slot
/// or any number of consecutive slots.
///
/// Every UTC offset in use worldwide is a multiple of 15 minutes, so a time
/// on the grid in UTC is also on the grid in any resource's local time.
/// </summary>
public static class TimeSlots
{
    public static readonly TimeSpan Length = TimeSpan.FromMinutes(15);

    /// <summary>True if <paramref name="time"/> is exactly on a slot boundary (no stray minutes, seconds or ticks).</summary>
    public static bool IsAligned(DateTime time) => time.Ticks % Length.Ticks == 0;

    /// <inheritdoc cref="IsAligned(DateTime)"/>
    public static bool IsAligned(TimeOnly time) => time.Ticks % Length.Ticks == 0;
}
