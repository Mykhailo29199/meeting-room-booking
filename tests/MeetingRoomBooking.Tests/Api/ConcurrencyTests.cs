using System.Net;
using System.Net.Http.Json;
using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Resources;
using MeetingRoomBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>
/// Task item 6: fire many simultaneous booking requests at the same slot and
/// assert that exactly one booking is created.
///
/// Every request is a real HTTP call through the whole API (authentication,
/// controller, service, EF Core, database), each from a different user, on
/// its own connection. All of them wait on one start signal and are released
/// together, so they genuinely race. The outcome is checked twice: in the HTTP
/// responses (exactly one 201, all others 409, never a 5xx) and in the
/// database (exactly one booking and its slots stored).
///
/// The scenarios run on SQLite — always, with no setup — and on SQL Server
/// with migrations and READ_COMMITTED_SNAPSHOT like Azure SQL, when
/// configured (see SqlServerTestDatabase). On SQLite, which serialises
/// writes, the scenarios check the API contract; the SQL Server run is what
/// exercises the concurrency control.
/// </summary>
public abstract class ConcurrencyTestsBase
{
    private const int ParallelRequests = 20;
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private readonly ApiFactoryBase _api;

    protected ConcurrencyTestsBase(ApiFactoryBase api) => _api = api;

    /// <summary>Everyone asks for exactly the same slots at the same moment.</summary>
    protected async Task Identical_requests_exactly_one_succeeds()
    {
        var (resource, schedule, users) = await ArrangeAsync();
        var request = Range(schedule, first: 40, count: 4);

        var responses = await FireSimultaneouslyAsync(users, _ => request);

        await AssertExactlyOneWinnerAsync(resource.Id, responses, expectedSlots: 4);
    }

    /// <summary>
    /// Everyone asks for a different range, but all ranges share one slot —
    /// the case a unique index on the booking's start time alone would miss.
    /// </summary>
    protected async Task Overlapping_requests_exactly_one_succeeds()
    {
        var (resource, schedule, users) = await ArrangeAsync();

        // Request i covers slots [40 - i % 4, 44 - i % 4): four different
        // ranges, all containing slot 40.
        var responses = await FireSimultaneouslyAsync(users, i => Range(schedule, first: 40 - i % 4, count: 4));

        await AssertExactlyOneWinnerAsync(resource.Id, responses, expectedSlots: 4);
    }

    /// <summary>The same race several times in a row, so a pass is not luck.</summary>
    protected async Task Repeated_races_each_have_exactly_one_winner()
    {
        var (resource, schedule, users) = await ArrangeAsync();

        for (var round = 0; round < 5; round++)
        {
            var request = Range(schedule, first: 8 * round, count: 2);
            var responses = await FireSimultaneouslyAsync(users, _ => request);

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
                r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        }

        await using var context = _api.Database.CreateContext();
        Assert.Equal(5, await context.Bookings.CountAsync(b => b.ResourceId == resource.Id));
        Assert.Equal(10, await context.BookingSlots.CountAsync(s => s.ResourceId == resource.Id));
    }

    private async Task<(ResourceDto Resource, ResourceScheduleDto Schedule, HttpClient[] Users)> ArrangeAsync()
    {
        var admin = await _api.CreateAdminClientAsync();
        var created = await admin.PostAsJsonAsync("/api/resources",
            new { name = $"Race room {Guid.NewGuid():N}", capacity = 4, timeZoneId = "Europe/Berlin", opensAt = "00:00", closesAt = "23:45" });
        created.EnsureSuccessStatusCode();
        var resource = (await created.Content.ReadFromJsonAsync<ResourceDto>())!;

        var tomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Berlin)).AddDays(1);
        var schedule = (await admin.GetFromJsonAsync<ResourceScheduleDto>(
            $"/api/resources/{resource.Id}/schedule?date={tomorrow:yyyy-MM-dd}"))!;

        var users = new HttpClient[ParallelRequests];
        for (var i = 0; i < users.Length; i++)
            users[i] = await _api.CreateUserClientAsync();

        return (resource, schedule, users);
    }

    private static CreateBookingRequest Range(ResourceScheduleDto schedule, int first, int count) =>
        new(schedule.ResourceId, schedule.Slots[first].StartUtc, schedule.Slots[first + count - 1].EndUtc);

    /// <summary>Starts one request per user, all held back until a single start signal.</summary>
    private static async Task<HttpResponseMessage[]> FireSimultaneouslyAsync(
        HttpClient[] users, Func<int, CreateBookingRequest> requestFor)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = users.Select((user, i) => Task.Run(async () =>
        {
            await start.Task;
            return await user.PostAsJsonAsync("/api/bookings", requestFor(i));
        })).ToArray();

        start.SetResult();
        return await Task.WhenAll(requests);
    }

    private async Task AssertExactlyOneWinnerAsync(Guid resourceId, HttpResponseMessage[] responses, int expectedSlots)
    {
        // HTTP: one success, every other request a clean conflict — no 5xx, no other status.
        var statuses = responses.Select(r => r.StatusCode).ToList();
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(ParallelRequests - 1, statuses.Count(s => s == HttpStatusCode.Conflict));

        var winner = (await responses.Single(r => r.StatusCode == HttpStatusCode.Created)
            .Content.ReadFromJsonAsync<BookingDto>())!;

        // Database: exactly the winner's booking and slots were stored.
        await using var context = _api.Database.CreateContext();
        var bookings = await context.Bookings.Where(b => b.ResourceId == resourceId).ToListAsync();
        var slots = await context.BookingSlots.Where(s => s.ResourceId == resourceId).ToListAsync();
        Assert.Equal(winner.Id, Assert.Single(bookings).Id);
        Assert.Equal(expectedSlots, slots.Count);
        Assert.All(slots, s => Assert.Equal(winner.Id, s.BookingId));
    }
}

/// <summary>The concurrency test on SQLite — runs everywhere with a plain `dotnet test`.</summary>
public sealed class ConcurrencyTests(ApiFactory api) : ConcurrencyTestsBase(api), IClassFixture<ApiFactory>
{
    [Fact]
    public Task Twenty_identical_requests_create_exactly_one_booking() => Identical_requests_exactly_one_succeeds();

    [Fact]
    public Task Twenty_overlapping_requests_create_exactly_one_booking() => Overlapping_requests_exactly_one_succeeds();

    [Fact]
    public Task Five_races_in_a_row_each_have_exactly_one_winner() => Repeated_races_each_have_exactly_one_winner();
}

/// <summary>The same concurrency test on SQL Server, as in production (opt-in).</summary>
public sealed class SqlServerConcurrencyTests(SqlServerApiFactory api) : ConcurrencyTestsBase(api), IClassFixture<SqlServerApiFactory>
{
    [SqlServerFact]
    public Task Twenty_identical_requests_create_exactly_one_booking() => Identical_requests_exactly_one_succeeds();

    [SqlServerFact]
    public Task Twenty_overlapping_requests_create_exactly_one_booking() => Overlapping_requests_exactly_one_succeeds();

    [SqlServerFact]
    public Task Five_races_in_a_row_each_have_exactly_one_winner() => Repeated_races_each_have_exactly_one_winner();
}
