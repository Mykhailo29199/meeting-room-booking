using MeetingRoomBooking.Application.Bookings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRoomBooking.Api.Realtime;

/// <summary>
/// Real-time schedule updates (task item 7). A client that shows a
/// resource's schedule connects to <see cref="Path"/>, calls
/// <see cref="WatchResource"/>, and from then on receives
/// <see cref="SlotsChangedEvent"/> whenever slots of that resource are booked
/// or freed — without refreshing. Uses Azure SignalR Service when configured.
///
/// The hub only manages who watches what; messages are sent by
/// <see cref="SignalRScheduleNotifier"/> after changes are committed.
/// Signed-in users only, like the schedule itself; browsers pass the token as
/// the <c>access_token</c> query parameter (see Program).
/// </summary>
[Authorize]
public sealed class ScheduleHub : Hub
{
    public const string Path = "/hubs/schedule";

    /// <summary>Client method name for <see cref="SlotsChangedMessage"/>.</summary>
    public const string SlotsChangedEvent = "SlotsChanged";

    public static string GroupFor(Guid resourceId) => $"resource:{resourceId}";

    /// <summary>Start receiving changes of this resource's slots.</summary>
    public Task WatchResource(Guid resourceId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(resourceId));

    /// <summary>Stop receiving changes of this resource's slots, e.g. when the user opens another one.</summary>
    public Task StopWatchingResource(Guid resourceId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(resourceId));
}

/// <summary>
/// Sent to everyone watching <see cref="ResourceId"/>: these slots are now
/// booked (<see cref="SlotChange.IsBooked"/> true) or free again. Never says
/// who booked.
/// </summary>
public sealed record SlotsChangedMessage(Guid ResourceId, IReadOnlyList<SlotChange> Slots);
