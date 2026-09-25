using MeetingRoomBooking.Application.Bookings;
using Microsoft.AspNetCore.SignalR;

namespace MeetingRoomBooking.Api.Realtime;

/// <summary><see cref="IScheduleNotifier"/> over SignalR (local, or Azure SignalR Service).</summary>
public sealed class SignalRScheduleNotifier : IScheduleNotifier
{
    private readonly IHubContext<ScheduleHub> _hub;
    private readonly ILogger<SignalRScheduleNotifier> _logger;

    public SignalRScheduleNotifier(IHubContext<ScheduleHub> hub, ILogger<SignalRScheduleNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task SlotsChangedAsync(Guid resourceId, IReadOnlyList<SlotChange> slots)
    {
        if (slots.Count == 0)
            return;

        try
        {
            // Not the request's cancellation token: the change is committed and
            // other viewers must hear about it even if the requester has gone.
            await _hub.Clients.Group(ScheduleHub.GroupFor(resourceId))
                .SendAsync(ScheduleHub.SlotsChangedEvent, new SlotsChangedMessage(resourceId, slots), CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The booking is saved; a lost notification must not turn the
            // request into an error. Viewers catch up on their next reload.
            _logger.LogWarning(ex, "Could not notify viewers of resource {ResourceId} about {Count} changed slots.",
                resourceId, slots.Count);
        }
    }
}
