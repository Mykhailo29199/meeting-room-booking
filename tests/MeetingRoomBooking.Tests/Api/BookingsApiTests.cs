using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Resources;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>
/// The booking endpoints over HTTP, used the way a client would: read the
/// schedule, send its UTC slot times back to book. Runs on the real clock, so
/// every booking is made for tomorrow in the resource's time zone. Business
/// rules in detail are covered by the service tests.
/// </summary>
public class BookingsApiTests : IClassFixture<ApiFactory>
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly ApiFactory _api;

    public BookingsApiTests(ApiFactory api) => _api = api;

    private static DateOnly TomorrowInBerlin() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Berlin)).AddDays(1);

    /// <summary>A new resource, open all day, so any slot tomorrow is bookable.</summary>
    private async Task<ResourceDto> CreateResourceAsync()
    {
        var admin = await _api.CreateAdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/resources",
            new { name = $"Room {Guid.NewGuid():N}", capacity = 4, timeZoneId = "Europe/Berlin", opensAt = "00:00", closesAt = "23:45" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResourceDto>())!;
    }

    private static async Task<ResourceScheduleDto> ScheduleAsync(HttpClient client, Guid resourceId) =>
        (await client.GetFromJsonAsync<ResourceScheduleDto>($"/api/resources/{resourceId}/schedule?date={TomorrowInBerlin():yyyy-MM-dd}"))!;

    /// <summary>Books the schedule's slots [from, from + count) — times taken straight from the schedule.</summary>
    private static async Task<HttpResponseMessage> BookAsync(HttpClient client, ResourceScheduleDto schedule, int from, int count) =>
        await client.PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(schedule.ResourceId, schedule.Slots[from].StartUtc, schedule.Slots[from + count - 1].EndUtc));

    [Fact]
    public async Task Schedule_lists_the_days_slots_in_UTC_with_the_resource_time_zone()
    {
        var resource = await CreateResourceAsync();
        var user = await _api.CreateUserClientAsync();

        var schedule = await ScheduleAsync(user, resource.Id);

        Assert.Equal("Europe/Berlin", schedule.TimeZoneId);
        Assert.Equal(TomorrowInBerlin(), schedule.LocalDate);
        Assert.Equal(
            TimeZoneInfo.ConvertTimeToUtc(TomorrowInBerlin().ToDateTime(TimeOnly.MinValue), Berlin),
            schedule.Slots[0].StartUtc);
        Assert.All(schedule.Slots, s => Assert.False(s.IsBooked));
    }

    [Fact]
    public async Task User_books_slots_taken_from_the_schedule()
    {
        var resource = await CreateResourceAsync();
        var user = await _api.CreateUserClientAsync();
        var schedule = await ScheduleAsync(user, resource.Id);

        var response = await BookAsync(user, schedule, from: 40, count: 4); // 4 consecutive slots

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = (await response.Content.ReadFromJsonAsync<BookingDto>())!;
        Assert.Equal($"/api/bookings/{booking.Id}", response.Headers.Location?.OriginalString);
        var after = await ScheduleAsync(user, resource.Id);
        Assert.All(after.Slots.Skip(40).Take(4), s =>
        {
            Assert.True(s.IsMine);
            Assert.Equal(booking.Id, s.BookingId);
        });
    }

    [Fact]
    public async Task Overlapping_booking_by_someone_else_is_a_409()
    {
        var resource = await CreateResourceAsync();
        var anna = await _api.CreateUserClientAsync();
        var bob = await _api.CreateUserClientAsync();
        var schedule = await ScheduleAsync(anna, resource.Id);
        await BookAsync(anna, schedule, from: 40, count: 4);

        var response = await BookAsync(bob, schedule, from: 43, count: 2); // shares only slot 43 with the first booking

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("someone else has just booked", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Time_without_UTC_marker_is_a_400_not_a_server_error()
    {
        var resource = await CreateResourceAsync();
        var user = await _api.CreateUserClientAsync();
        var day = TomorrowInBerlin().ToString("yyyy-MM-dd");

        var response = await user.PostAsJsonAsync("/api/bookings",
            new { resourceId = resource.Id, startUtc = $"{day}T10:00:00", endUtc = $"{day}T11:00:00" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("StartUtc", out _));
    }

    [Fact]
    public async Task Only_the_owner_or_an_admin_can_cancel_and_cancelling_frees_the_slots()
    {
        var resource = await CreateResourceAsync();
        var anna = await _api.CreateUserClientAsync();
        var bob = await _api.CreateUserClientAsync();
        var schedule = await ScheduleAsync(anna, resource.Id);
        var booking = (await (await BookAsync(anna, schedule, 40, 4)).Content.ReadFromJsonAsync<BookingDto>())!;

        Assert.Equal(HttpStatusCode.Forbidden, (await bob.DeleteAsync($"/api/bookings/{booking.Id}")).StatusCode);

        var cancel = await anna.DeleteAsync($"/api/bookings/{booking.Id}");

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.True((await cancel.Content.ReadFromJsonAsync<CancellationResult>())!.CancelledCompletely);
        Assert.Equal(HttpStatusCode.Created, (await BookAsync(bob, schedule, 40, 4)).StatusCode);
    }

    [Fact]
    public async Task Users_see_their_own_bookings_admins_see_everyones()
    {
        var resource = await CreateResourceAsync();
        var anna = await _api.RegisterAsync("Anna");
        var bob = await _api.RegisterAsync("Bob");
        var annaClient = _api.CreateClient(anna.AccessToken);
        var bobClient = _api.CreateClient(bob.AccessToken);
        var schedule = await ScheduleAsync(annaClient, resource.Id);
        var annas = (await (await BookAsync(annaClient, schedule, 40, 4)).Content.ReadFromJsonAsync<BookingDto>())!;
        var bobs = (await (await BookAsync(bobClient, schedule, 50, 2)).Content.ReadFromJsonAsync<BookingDto>())!;

        var mine = (await annaClient.GetFromJsonAsync<List<BookingListItem>>("/api/bookings/mine"))!;
        Assert.Contains(mine, b => b.Id == annas.Id);
        Assert.DoesNotContain(mine, b => b.Id == bobs.Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await annaClient.GetAsync("/api/bookings")).StatusCode);

        var admin = await _api.CreateAdminClientAsync();
        var all = (await admin.GetFromJsonAsync<List<BookingListItem>>($"/api/bookings?resourceId={resource.Id}"))!;
        Assert.Equal([annas.Id, bobs.Id], all.Select(b => b.Id));
        Assert.Equal(["Anna", "Bob"], all.Select(b => b.UserDisplayName));
        Assert.Equal([anna.Email, bob.Email], all.Select(b => b.UserEmail));
        Assert.All(all, b => Assert.Equal(resource.Name, b.ResourceName));
    }

    [Fact]
    public async Task Removed_resource_cannot_be_booked()
    {
        var resource = await CreateResourceAsync();
        var user = await _api.CreateUserClientAsync();
        var schedule = await ScheduleAsync(user, resource.Id);
        var admin = await _api.CreateAdminClientAsync();
        await admin.DeleteAsync($"/api/resources/{resource.Id}");

        var response = await BookAsync(user, schedule, 40, 4);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/resources/{resource.Id}/schedule")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_book()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/bookings",
            new CreateBookingRequest(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
