using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using MeetingRoomBooking.Api.Realtime;
using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Resources;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>
/// Task item 7 end to end: a viewer connected to the schedule hub sees
/// another user's booking or cancellation immediately, without asking again.
///
/// The viewer connects like a browser does — WebSocket, token in the
/// access_token query parameter — to the same in-memory API that handles the
/// HTTP requests. (In Azure the WebSocket goes through Azure SignalR Service;
/// the hub and the code that sends are the same.)
/// </summary>
public class RealtimeTests : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly ApiFactory _api;

    public RealtimeTests(ApiFactory api) => _api = api;

    /// <summary>A signed-in viewer watching <paramref name="resourceId"/>, and the messages it receives.</summary>
    private sealed class Viewer : IAsyncDisposable
    {
        public required HubConnection Connection { get; init; }
        public Channel<SlotsChangedMessage> Messages { get; } = Channel.CreateUnbounded<SlotsChangedMessage>();

        public async Task<SlotsChangedMessage> NextAsync()
        {
            using var timeout = new CancellationTokenSource(MessageTimeout);
            return await Messages.Reader.ReadAsync(timeout.Token);
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }

    private HubConnection Connect(string? accessToken)
    {
        var server = _api.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, ScheduleHub.Path.TrimStart('/')), options =>
            {
                // A browser connects over WebSocket and, unable to set headers,
                // sends the token as ?access_token=. The .NET client would send
                // a header instead, so build the browser's URL explicitly: this
                // is the path the Angular client uses.
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var uri = accessToken is null
                        ? context.Uri
                        : new UriBuilder(context.Uri) { Query = $"access_token={Uri.EscapeDataString(accessToken)}" }.Uri;
                    return await server.CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
                };
            })
            .Build();
    }

    private async Task<Viewer> WatchAsync(Guid resourceId)
    {
        var token = (await _api.RegisterAsync("Viewer")).AccessToken;
        var viewer = new Viewer { Connection = Connect(token) };
        viewer.Connection.On<SlotsChangedMessage>(ScheduleHub.SlotsChangedEvent, m => viewer.Messages.Writer.TryWrite(m));
        await viewer.Connection.StartAsync();
        await viewer.Connection.InvokeAsync(nameof(ScheduleHub.WatchResource), resourceId);
        return viewer;
    }

    private async Task<(ResourceDto Resource, ResourceScheduleDto Schedule)> CreateResourceAsync()
    {
        var admin = await _api.CreateAdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/resources",
            new { name = $"Live room {Guid.NewGuid():N}", capacity = 4, timeZoneId = "Europe/Berlin", opensAt = "00:00", closesAt = "23:45" });
        created.EnsureSuccessStatusCode();
        var resource = (await created.Content.ReadFromJsonAsync<ResourceDto>())!;
        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Berlin)).AddDays(1);
        var schedule = (await admin.GetFromJsonAsync<ResourceScheduleDto>(
            $"/api/resources/{resource.Id}/schedule?date={tomorrow:yyyy-MM-dd}"))!;
        return (resource, schedule);
    }

    private static CreateBookingRequest Range(ResourceScheduleDto schedule, int first, int count) =>
        new(schedule.ResourceId, schedule.Slots[first].StartUtc, schedule.Slots[first + count - 1].EndUtc);

    [Fact]
    public async Task Viewer_sees_someone_elses_booking_immediately()
    {
        var (resource, schedule) = await CreateResourceAsync();
        await using var viewer = await WatchAsync(resource.Id);
        var booker = await _api.CreateUserClientAsync();

        (await booker.PostAsJsonAsync("/api/bookings", Range(schedule, 40, 2))).EnsureSuccessStatusCode();

        var message = await viewer.NextAsync();
        Assert.Equal(resource.Id, message.ResourceId);
        Assert.Equal(schedule.Slots.Skip(40).Take(2).Select(s => s.StartUtc), message.Slots.Select(s => s.StartUtc));
        Assert.All(message.Slots, s => Assert.True(s.IsBooked));
    }

    [Fact]
    public async Task Viewer_sees_a_cancellation_immediately()
    {
        var (resource, schedule) = await CreateResourceAsync();
        var booker = await _api.CreateUserClientAsync();
        var booking = (await (await booker.PostAsJsonAsync("/api/bookings", Range(schedule, 40, 2)))
            .Content.ReadFromJsonAsync<BookingDto>())!;
        await using var viewer = await WatchAsync(resource.Id);

        (await booker.DeleteAsync($"/api/bookings/{booking.Id}")).EnsureSuccessStatusCode();

        var message = await viewer.NextAsync();
        Assert.Equal(2, message.Slots.Count);
        Assert.All(message.Slots, s => Assert.False(s.IsBooked));
    }

    [Fact]
    public async Task Viewer_hears_only_about_the_resource_it_watches()
    {
        var (watched, _) = await CreateResourceAsync();
        var (other, otherSchedule) = await CreateResourceAsync();
        await using var viewer = await WatchAsync(watched.Id);
        var booker = await _api.CreateUserClientAsync();

        (await booker.PostAsJsonAsync("/api/bookings", Range(otherSchedule, 40, 1))).EnsureSuccessStatusCode();
        await Task.Delay(QuietPeriod);

        Assert.False(viewer.Messages.Reader.TryRead(out _), $"Nothing about {other.Id} should reach a viewer of {watched.Id}.");
    }

    [Fact]
    public async Task Hub_refuses_connections_without_a_token()
    {
        await using var anonymous = Connect(accessToken: null);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => anonymous.StartAsync());

        Assert.Contains("401", error.Message);
    }

    [Fact]
    public async Task Token_in_the_query_string_is_accepted_on_the_hub_only()
    {
        var token = (await _api.RegisterAsync()).AccessToken;

        var response = await _api.CreateClient().GetAsync($"/api/auth/me?access_token={token}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
