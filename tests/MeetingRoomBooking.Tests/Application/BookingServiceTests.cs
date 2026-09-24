using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Domain.Resources;
using MeetingRoomBooking.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Application;

public class BookingServiceTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo BerlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateOnly BookingDay = new(2026, 10, 1);
    private static readonly DateTime DayBeforeUtc = new(2026, 9, 30, 7, 0, 0, DateTimeKind.Utc);

    private static readonly UserContext Alice = new("alice", IsAdmin: false);
    private static readonly UserContext Bob = new("bob", IsAdmin: false);
    private static readonly UserContext Admin = new("admin", IsAdmin: true);

    private SqliteTestDatabase _database = null!;
    private Resource _resource = null!;

    public async Task InitializeAsync()
    {
        _database = await SqliteTestDatabase.CreateAsync();
        _resource = new Resource("Sunflower", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));
        await using var context = _database.CreateContext();
        context.Resources.Add(_resource);
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    /// <summary>UTC instant of a Berlin wall-clock time on the booking day.</summary>
    private static DateTime Berlin(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(BookingDay.ToDateTime(new TimeOnly(hour, minute)), BerlinZone);

    /// <summary>
    /// Runs <paramref name="action"/> with a fresh service, unit of work and
    /// database connection — like one HTTP request — at time <paramref name="nowUtc"/>.
    /// </summary>
    private async Task<T> AsRequest<T>(Func<BookingService, Task<T>> action, DateTime? nowUtc = null)
    {
        await using var context = _database.CreateContext();
        var service = new BookingService(
            SqliteTestDatabase.CreateUnitOfWork(context), new FixedTimeProvider(nowUtc ?? DayBeforeUtc));
        return await action(service);
    }

    private Task AsRequest(Func<BookingService, Task> action, DateTime? nowUtc = null) =>
        AsRequest(async service => { await action(service); return true; }, nowUtc);

    private Task<BookingDto> BookAsync(UserContext user, DateTime startUtc, DateTime endUtc) =>
        AsRequest(s => s.CreateAsync(new CreateBookingRequest(_resource.Id, startUtc, endUtc), user));

    private async Task<int> SlotCountAsync()
    {
        await using var context = _database.CreateContext();
        return await context.BookingSlots.CountAsync(s => s.ResourceId == _resource.Id);
    }

    // ---- Create ------------------------------------------------------------

    [Fact]
    public async Task Create_saves_the_booking_with_its_slots()
    {
        var booking = await BookAsync(Alice, Berlin(10, 0), Berlin(11, 0));

        Assert.Equal("alice", booking.UserId);
        Assert.Equal(Berlin(10, 0), booking.StartUtc);
        Assert.Equal(4, await SlotCountAsync());
    }

    [Fact]
    public async Task Create_for_an_unknown_resource_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => AsRequest(s =>
            s.CreateAsync(new CreateBookingRequest(Guid.NewGuid(), Berlin(10, 0), Berlin(11, 0)), Alice)));
    }

    [Fact]
    public async Task Create_that_breaks_a_rule_is_rejected()
    {
        // Before opening time.
        await Assert.ThrowsAsync<DomainException>(() => BookAsync(Alice, Berlin(7, 0), Berlin(8, 0)));
    }

    [Fact]
    public async Task Create_overlapping_a_taken_slot_is_a_conflict_and_saves_nothing()
    {
        await BookAsync(Alice, Berlin(10, 0), Berlin(11, 0));

        var conflict = await Assert.ThrowsAsync<ConflictException>(() => BookAsync(Bob, Berlin(10, 45), Berlin(11, 30)));

        Assert.Contains("someone else", conflict.Message);
        Assert.Equal(4, await SlotCountAsync());
    }

    // ---- Cancel ------------------------------------------------------------

    [Fact]
    public async Task Cancelling_before_the_start_removes_the_booking_and_frees_its_slots()
    {
        var booking = await BookAsync(Alice, Berlin(10, 0), Berlin(11, 0));

        var remaining = await AsRequest(s => s.CancelAsync(booking.Id, Alice));

        Assert.Null(remaining);
        Assert.Equal(0, await SlotCountAsync());
        await using (var context = _database.CreateContext())
        {
            Assert.False(await context.Bookings.AnyAsync(b => b.Id == booking.Id));
        }
        await BookAsync(Bob, Berlin(10, 0), Berlin(11, 0)); // the same time is bookable again
    }

    [Fact]
    public async Task Finishing_early_frees_the_rest_of_the_day_for_others()
    {
        // Alice booked 08:00–20:00 and leaves at 14:05.
        var booking = await BookAsync(Alice, Berlin(8, 0), Berlin(20, 0));

        var remaining = await AsRequest(s => s.CancelAsync(booking.Id, Alice), nowUtc: Berlin(14, 5));

        Assert.NotNull(remaining);
        Assert.Equal(Berlin(14, 15), remaining.EndUtc);
        Assert.Equal(25, await SlotCountAsync()); // 08:00 ... 14:00 kept as history

        // The slot in progress is still Alice's; everything after it is free.
        await Assert.ThrowsAsync<ConflictException>(() =>
            AsRequest(s => s.CreateAsync(new CreateBookingRequest(_resource.Id, Berlin(14, 0), Berlin(14, 15)), Bob),
                nowUtc: Berlin(13, 55)));
        var bobs = await AsRequest(s =>
            s.CreateAsync(new CreateBookingRequest(_resource.Id, Berlin(14, 15), Berlin(20, 0)), Bob),
            nowUtc: Berlin(14, 10));
        Assert.Equal(Berlin(14, 15), bobs.StartUtc);
    }

    [Fact]
    public async Task Stored_booking_reflects_the_shortened_end()
    {
        var booking = await BookAsync(Alice, Berlin(8, 0), Berlin(20, 0));

        await AsRequest(s => s.CancelAsync(booking.Id, Alice), nowUtc: Berlin(14, 5));

        await using var context = _database.CreateContext();
        var stored = await context.Bookings.Include(b => b.Slots).SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(Berlin(14, 15), stored.EndUtc);
        Assert.Equal(25, stored.Slots.Count);
    }

    [Fact]
    public async Task Cancelling_someone_elses_booking_is_forbidden()
    {
        var booking = await BookAsync(Alice, Berlin(10, 0), Berlin(11, 0));

        await Assert.ThrowsAsync<ForbiddenException>(() => AsRequest(s => s.CancelAsync(booking.Id, Bob)));
        Assert.Equal(4, await SlotCountAsync());
    }

    [Fact]
    public async Task Admin_can_cancel_any_booking()
    {
        var booking = await BookAsync(Alice, Berlin(10, 0), Berlin(11, 0));

        await AsRequest(s => s.CancelAsync(booking.Id, Admin));

        Assert.Equal(0, await SlotCountAsync());
    }

    [Fact]
    public async Task Cancelling_an_unknown_booking_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => AsRequest(s => s.CancelAsync(Guid.NewGuid(), Alice)));
    }

    [Fact]
    public async Task Finished_booking_cannot_be_released()
    {
        var booking = await BookAsync(Alice, Berlin(10, 0), Berlin(11, 0));

        await Assert.ThrowsAsync<DomainException>(() =>
            AsRequest(s => s.CancelAsync(booking.Id, Alice), nowUtc: Berlin(12, 0)));
        Assert.Equal(4, await SlotCountAsync());
    }

    // ---- Schedule ----------------------------------------------------------

    [Fact]
    public async Task Schedule_lists_every_slot_of_the_day_with_its_status()
    {
        var booking = await BookAsync(Alice, Berlin(10, 0), Berlin(10, 30));

        var schedule = await AsRequest(s => s.GetScheduleAsync(_resource.Id, BookingDay, Alice));

        Assert.Equal("Sunflower", schedule.ResourceName);
        Assert.Equal("Europe/Berlin", schedule.TimeZoneId);
        Assert.Equal(48, schedule.Slots.Count);
        Assert.Equal(Berlin(8, 0), schedule.Slots[0].StartUtc);
        Assert.Equal(Berlin(20, 0), schedule.Slots[^1].EndUtc);

        var booked = schedule.Slots.Where(s => s.IsBooked).ToList();
        Assert.Equal([Berlin(10, 0), Berlin(10, 15)], booked.Select(s => s.StartUtc));
        Assert.All(booked, s =>
        {
            Assert.True(s.IsMine);
            Assert.Equal(booking.Id, s.BookingId);
        });
    }

    [Fact]
    public async Task Schedule_does_not_reveal_other_users_bookings()
    {
        await BookAsync(Alice, Berlin(10, 0), Berlin(10, 30));

        var schedule = await AsRequest(s => s.GetScheduleAsync(_resource.Id, BookingDay, Bob));

        var booked = schedule.Slots.Where(s => s.IsBooked).ToList();
        Assert.Equal(2, booked.Count);
        Assert.All(booked, s =>
        {
            Assert.False(s.IsMine);
            Assert.Null(s.BookingId);
        });
    }

    [Fact]
    public async Task Schedule_marks_slots_that_have_already_started_as_past()
    {
        var schedule = await AsRequest(
            s => s.GetScheduleAsync(_resource.Id, BookingDay, Alice), nowUtc: Berlin(10, 5));

        // 08:00 ... 10:00 have started by 10:05.
        Assert.Equal(9, schedule.Slots.Count(s => s.IsPast));
        Assert.False(schedule.Slots.Single(s => s.StartUtc == Berlin(10, 15)).IsPast);
    }

    [Fact]
    public async Task Schedule_of_an_unknown_resource_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            AsRequest(s => s.GetScheduleAsync(Guid.NewGuid(), BookingDay, Alice)));
    }
}

/// <summary>A clock that always shows the same time.</summary>
internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
