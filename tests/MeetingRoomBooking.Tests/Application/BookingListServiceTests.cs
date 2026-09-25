using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Domain.Resources;
using MeetingRoomBooking.Infrastructure.Persistence;
using MeetingRoomBooking.Tests.Infrastructure;

namespace MeetingRoomBooking.Tests.Application;

public class BookingListServiceTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo BerlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateTime DayBeforeUtc = new(2026, 9, 30, 7, 0, 0, DateTimeKind.Utc);
    private static readonly UserContext Alice = new("alice", IsAdmin: false);
    private static readonly UserContext Bob = new("bob", IsAdmin: false);

    private SqliteTestDatabase _database = null!;
    private Resource _sunflower = null!;
    private Resource _tulip = null!;

    public async Task InitializeAsync()
    {
        _database = await SqliteTestDatabase.CreateAsync();
        _sunflower = new Resource("Sunflower", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));
        _tulip = new Resource("Tulip", 4, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));
        await using var context = _database.CreateContext();
        context.Resources.AddRange(_sunflower, _tulip);
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static DateTime Berlin(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 1, hour, minute, 0), BerlinZone);

    private async Task<Guid> BookAsync(Resource resource, UserContext user, int startHour, int endHour)
    {
        await using var context = _database.CreateContext();
        var service = new BookingService(SqliteTestDatabase.CreateUnitOfWork(context), new FixedTimeProvider(DayBeforeUtc), new RecordingScheduleNotifier());
        var booking = await service.CreateAsync(
            new CreateBookingRequest(resource.Id, Berlin(startHour, 0), Berlin(endHour, 0)), user);
        return booking.Id;
    }

    private async Task<IReadOnlyList<BookingListItem>> ListAsync(
        Func<BookingListService, Task<IReadOnlyList<BookingListItem>>> list, DateTime nowUtc)
    {
        await using var context = _database.CreateContext();
        return await list(new BookingListService(new BookingQueries(context), new FixedTimeProvider(nowUtc)));
    }

    [Fact]
    public async Task Finished_bookings_are_listed_only_on_request()
    {
        var morning = await BookAsync(_sunflower, Alice, 8, 9);
        var afternoon = await BookAsync(_sunflower, Alice, 14, 15);
        var noon = Berlin(12, 0);

        var upcoming = await ListAsync(s => s.ListMineAsync(Alice, includePast: false), noon);
        var everything = await ListAsync(s => s.ListMineAsync(Alice, includePast: true), noon);

        Assert.Equal([afternoon], upcoming.Select(b => b.Id));
        Assert.Equal([morning, afternoon], everything.Select(b => b.Id));
    }

    [Fact]
    public async Task Booking_under_way_counts_as_upcoming()
    {
        var ongoing = await BookAsync(_sunflower, Alice, 10, 12);

        var upcoming = await ListAsync(s => s.ListMineAsync(Alice, includePast: false), Berlin(11, 0));

        Assert.Equal([ongoing], upcoming.Select(b => b.Id));
    }

    [Fact]
    public async Task Mine_lists_only_the_callers_bookings()
    {
        var alices = await BookAsync(_sunflower, Alice, 10, 11);
        await BookAsync(_tulip, Bob, 10, 11);

        var mine = await ListAsync(s => s.ListMineAsync(Alice, includePast: true), DayBeforeUtc);

        Assert.Equal([alices], mine.Select(b => b.Id));
    }

    [Fact]
    public async Task All_lists_everyones_bookings_optionally_for_one_resource()
    {
        var alices = await BookAsync(_sunflower, Alice, 10, 11);
        var bobs = await BookAsync(_tulip, Bob, 10, 11);

        var all = await ListAsync(s => s.ListAllAsync(resourceId: null, includePast: true), DayBeforeUtc);
        var tulipOnly = await ListAsync(s => s.ListAllAsync(_tulip.Id, includePast: true), DayBeforeUtc);

        Assert.Equal([alices, bobs], all.Select(b => b.Id).Order());
        Assert.Equal([bobs], tulipOnly.Select(b => b.Id));
        Assert.Equal("Tulip", tulipOnly[0].ResourceName);
        Assert.Null(tulipOnly[0].UserEmail); // "bob" has no account in this test: left join, no crash
    }
}
