using MeetingRoomBooking.Api.Realtime;
using MeetingRoomBooking.Application.Bookings;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeetingRoomBooking.Tests.Api;

public class SignalRScheduleNotifierTests
{
    [Fact]
    public async Task A_failed_broadcast_does_not_fail_the_request()
    {
        // The booking is already committed when notifying; if SignalR is down,
        // the user must still get their 201, not a 500.
        var notifier = new SignalRScheduleNotifier(new BrokenHubContext(), NullLogger<SignalRScheduleNotifier>.Instance);
        var slot = new SlotChange(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(15), true);

        var exception = await Record.ExceptionAsync(() => notifier.SlotsChangedAsync(Guid.NewGuid(), [slot]));

        Assert.Null(exception);
    }

    private sealed class BrokenHubContext : IHubContext<ScheduleHub>
    {
        public IHubClients Clients { get; } = new BrokenClients();
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class BrokenClients : IHubClients
        {
            private static readonly IClientProxy Broken = new BrokenProxy();
            public IClientProxy All => Broken;
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Broken;
            public IClientProxy Client(string connectionId) => Broken;
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Broken;
            public IClientProxy Group(string groupName) => Broken;
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Broken;
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => Broken;
            public IClientProxy User(string userId) => Broken;
            public IClientProxy Users(IReadOnlyList<string> userIds) => Broken;
        }

        private sealed class BrokenProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                throw new InvalidOperationException("SignalR is unavailable.");
        }
    }
}
