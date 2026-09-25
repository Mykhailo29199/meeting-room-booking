using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Application.Resources;
using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Application;

public class ResourceServiceTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo BerlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateOnly BookingDay = new(2026, 10, 1);
    private static readonly DateTime DayBeforeUtc = new(2026, 9, 30, 7, 0, 0, DateTimeKind.Utc);

    private static readonly UserContext Alice = new("alice", IsAdmin: false);
    private static readonly UserContext Bob = new("bob", IsAdmin: false);
    private static readonly UserContext Admin = new("admin", IsAdmin: true);

    private SqliteTestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await SqliteTestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    private static DateTime Berlin(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(BookingDay.ToDateTime(new TimeOnly(hour, minute)), BerlinZone);

    private static CreateResourceRequest Sunflower() =>
        new("Sunflower", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));

    /// <summary>Runs <paramref name="action"/> like one HTTP request, at time <paramref name="nowUtc"/>.</summary>
    private async Task<T> AsRequest<T>(Func<ResourceService, BookingService, Task<T>> action, DateTime? nowUtc = null)
    {
        await using var context = _database.CreateContext();
        var unitOfWork = SqliteTestDatabase.CreateUnitOfWork(context);
        var time = new FixedTimeProvider(nowUtc ?? DayBeforeUtc);
        return await action(new ResourceService(unitOfWork, time), new BookingService(unitOfWork, time));
    }

    private Task<ResourceDto> CreateAsync(CreateResourceRequest? request = null) =>
        AsRequest((resources, _) => resources.CreateAsync(request ?? Sunflower()));

    private Task<BookingDto> BookAsync(Guid resourceId, UserContext user, DateTime startUtc, DateTime endUtc) =>
        AsRequest((_, bookings) => bookings.CreateAsync(new CreateBookingRequest(resourceId, startUtc, endUtc), user));

    // ---- Create / read ------------------------------------------------------

    [Fact]
    public async Task Created_resource_is_active_and_has_a_version()
    {
        var created = await CreateAsync();

        Assert.True(created.IsActive);
        Assert.Equal("Europe/Berlin", created.TimeZoneId);
        Assert.NotEqual(Guid.Empty, created.Version);
    }

    [Fact]
    public async Task Invalid_resource_is_rejected()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            CreateAsync(new CreateResourceRequest("Sunflower", 0, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0))));
    }

    [Fact]
    public async Task Users_see_only_bookable_resources_admins_see_all()
    {
        var active = await CreateAsync();
        var removed = await CreateAsync(Sunflower() with { Name = "Old storage room" });
        await AsRequest((resources, _) => resources.RemoveAsync(removed.Id));

        var forUser = await AsRequest((resources, _) => resources.ListAsync(Alice));
        var forAdmin = await AsRequest((resources, _) => resources.ListAsync(Admin));

        Assert.Equal([active.Id], forUser.Select(r => r.Id));
        Assert.Equal(2, forAdmin.Count);
        Assert.All(forAdmin, r => Assert.NotEqual(Guid.Empty, r.Version));
    }

    [Fact]
    public async Task Removed_resource_is_hidden_from_users_but_visible_to_admins()
    {
        var resource = await CreateAsync();
        await AsRequest((resources, _) => resources.RemoveAsync(resource.Id));

        await Assert.ThrowsAsync<NotFoundException>(() => AsRequest((resources, _) => resources.GetAsync(resource.Id, Alice)));
        var forAdmin = await AsRequest((resources, _) => resources.GetAsync(resource.Id, Admin));
        Assert.False(forAdmin.IsActive);
    }

    // ---- Update (optimistic concurrency) ------------------------------------

    [Fact]
    public async Task Update_with_the_current_version_succeeds_and_changes_the_version()
    {
        var resource = await CreateAsync();

        var updated = await AsRequest((resources, _) => resources.UpdateAsync(resource.Id,
            new UpdateResourceRequest("Sunflower XL", 10, "Europe/Berlin", new TimeOnly(7, 0), new TimeOnly(21, 0), resource.Version)));

        Assert.Equal("Sunflower XL", updated.Name);
        Assert.Equal(10, updated.Capacity);
        Assert.NotEqual(resource.Version, updated.Version);
    }

    [Fact]
    public async Task Update_based_on_a_stale_version_is_a_conflict_and_changes_nothing()
    {
        var resource = await CreateAsync();
        // Admin A saves first...
        await AsRequest((resources, _) => resources.UpdateAsync(resource.Id,
            new UpdateResourceRequest("Sunflower (renovated)", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0), resource.Version)));

        // ...admin B still has the version from before A's change.
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => AsRequest((resources, _) => resources.UpdateAsync(resource.Id,
            new UpdateResourceRequest("Sunflower", 12, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0), resource.Version))));

        Assert.Contains("changed by someone else", conflict.Message);
        var stored = await AsRequest((resources, _) => resources.GetAsync(resource.Id, Admin));
        Assert.Equal("Sunflower (renovated)", stored.Name);
        Assert.Equal(6, stored.Capacity);
    }

    [Fact]
    public async Task Updating_an_unknown_resource_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => AsRequest((resources, _) => resources.UpdateAsync(Guid.NewGuid(),
            new UpdateResourceRequest("X", 1, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0), Guid.NewGuid()))));
    }

    // ---- Remove / restore ---------------------------------------------------

    [Fact]
    public async Task Removing_cancels_future_bookings_shortens_ongoing_ones_and_keeps_the_past()
    {
        var resource = await CreateAsync();
        var past = await BookAsync(resource.Id, Alice, Berlin(8, 0), Berlin(9, 0));
        var ongoing = await BookAsync(resource.Id, Bob, Berlin(10, 0), Berlin(12, 0));
        var future = await BookAsync(resource.Id, Alice, Berlin(14, 0), Berlin(15, 0));

        var result = await AsRequest((resources, _) => resources.RemoveAsync(resource.Id), nowUtc: Berlin(10, 5));

        Assert.Equal(new ResourceRemovalResult(CancelledBookings: 1, ShortenedBookings: 1), result);
        await using var context = _database.CreateContext();
        var bookings = await context.Bookings.Include(b => b.Slots).ToDictionaryAsync(b => b.Id);
        Assert.Equal(4, bookings[past.Id].Slots.Count);          // history kept
        Assert.Equal(Berlin(10, 15), bookings[ongoing.Id].EndUtc); // slot in progress kept, rest freed
        Assert.Single(bookings[ongoing.Id].Slots);
        Assert.False(bookings.ContainsKey(future.Id));             // cancelled
    }

    [Fact]
    public async Task Booking_with_only_its_current_slot_left_is_not_touched()
    {
        var resource = await CreateAsync();
        var booking = await BookAsync(resource.Id, Alice, Berlin(10, 0), Berlin(10, 15));

        var result = await AsRequest((resources, _) => resources.RemoveAsync(resource.Id), nowUtc: Berlin(10, 5));

        Assert.Equal(new ResourceRemovalResult(0, 0), result);
        await using var context = _database.CreateContext();
        Assert.True(await context.Bookings.AnyAsync(b => b.Id == booking.Id));
    }

    [Fact]
    public async Task Removed_resource_cannot_be_booked_until_it_is_restored()
    {
        var resource = await CreateAsync();
        await AsRequest((resources, _) => resources.RemoveAsync(resource.Id));

        await Assert.ThrowsAsync<DomainException>(() => BookAsync(resource.Id, Alice, Berlin(10, 0), Berlin(11, 0)));

        var restored = await AsRequest((resources, _) => resources.RestoreAsync(resource.Id));
        Assert.True(restored.IsActive);
        await BookAsync(resource.Id, Alice, Berlin(10, 0), Berlin(11, 0));
    }

    [Fact]
    public async Task Removing_an_unknown_resource_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => AsRequest((resources, _) => resources.RemoveAsync(Guid.NewGuid())));
    }
}
