namespace MeetingRoomBooking.Application.Bookings;

/// <summary>One slot whose booking status changed.</summary>
public sealed record SlotChange(DateTime StartUtc, DateTime EndUtc, bool IsBooked);

/// <summary>
/// Tells everyone currently viewing a resource's schedule that some of its
/// slots changed (task item 7). Implemented in the API with SignalR.
///
/// Called only after the change is committed, so viewers never see a state
/// that was rolled back. Deliberately does not say who booked: schedules
/// never reveal other users' bookings.
/// </summary>
public interface IScheduleNotifier
{
    /// <summary>
    /// Must not throw: the change is already saved, and failing to notify must
    /// not turn a successful request into an error. Implementations log instead.
    /// </summary>
    Task SlotsChangedAsync(Guid resourceId, IReadOnlyList<SlotChange> slots);
}
