using System.Collections.Concurrent;
using MeetingRoomBooking.Application.Bookings;

namespace MeetingRoomBooking.Tests.Application;

/// <summary>An <see cref="IScheduleNotifier"/> that remembers what would have been sent.</summary>
internal sealed class RecordingScheduleNotifier : IScheduleNotifier
{
    private readonly ConcurrentQueue<(Guid ResourceId, IReadOnlyList<SlotChange> Slots)> _sent = new();

    public IReadOnlyList<(Guid ResourceId, IReadOnlyList<SlotChange> Slots)> Sent => [.. _sent];

    public Task SlotsChangedAsync(Guid resourceId, IReadOnlyList<SlotChange> slots)
    {
        _sent.Enqueue((resourceId, slots));
        return Task.CompletedTask;
    }
}
