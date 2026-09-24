using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain.Bookings;
using MeetingRoomBooking.Domain.Resources;
using MeetingRoomBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Infrastructure;

public class PersistenceTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo BerlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateTime NowUtc = new(2026, 9, 30, 7, 0, 0, DateTimeKind.Utc);

    private SqliteTestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await SqliteTestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    /// <summary>UTC instant of a Berlin wall-clock time on 2026-10-01.</summary>
    private static DateTime Berlin(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 1).AddHours(hour).AddMinutes(minute), BerlinZone);

    private static Resource NewResource(string name = "Sunflower") =>
        new(name, 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));

    private async Task<Resource> SaveResourceAsync(Resource resource)
    {
        await using var context = _database.CreateContext();
        var unitOfWork = SqliteTestDatabase.CreateUnitOfWork(context);
        unitOfWork.Resources.Add(resource);
        await unitOfWork.CompleteAsync();
        return resource;
    }

    /// <summary>Saves a booking the way a request would: its own context, one commit.</summary>
    private async Task SaveBookingAsync(Booking booking)
    {
        await using var context = _database.CreateContext();
        var unitOfWork = SqliteTestDatabase.CreateUnitOfWork(context);
        unitOfWork.Bookings.Add(booking);
        await unitOfWork.CompleteAsync();
    }

    private async Task<List<BookingSlot>> SlotsOfAsync(Guid resourceId)
    {
        await using var context = _database.CreateContext();
        return await context.BookingSlots
            .Where(s => s.ResourceId == resourceId)
            .OrderBy(s => s.SlotStartUtc)
            .ToListAsync();
    }

    [Fact]
    public void Slot_primary_key_is_resource_and_slot_start()
    {
        // Guards the configuration the whole concurrency design rests on.
        using var context = _database.CreateContext();

        var key = context.Model.FindEntityType(typeof(BookingSlot))!.FindPrimaryKey()!;

        Assert.Equal([nameof(BookingSlot.ResourceId), nameof(BookingSlot.SlotStartUtc)], key.Properties.Select(p => p.Name));
    }

    [Fact]
    public async Task Resource_round_trips()
    {
        var saved = await SaveResourceAsync(NewResource());

        await using var context = _database.CreateContext();
        var loaded = await context.Resources.SingleAsync(r => r.Id == saved.Id);

        Assert.Equal("Sunflower", loaded.Name);
        Assert.Equal(6, loaded.Capacity);
        Assert.Equal("Europe/Berlin", loaded.TimeZoneId);
        Assert.Equal(new TimeOnly(8, 0), loaded.OpensAt);
        Assert.Equal(new TimeOnly(20, 0), loaded.ClosesAt);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task Booking_round_trips_with_its_slots_as_UTC()
    {
        var resource = await SaveResourceAsync(NewResource());
        var booking = Booking.Create(resource, "user-1", Berlin(10, 0), Berlin(10, 30), NowUtc);

        await SaveBookingAsync(booking);

        await using var context = _database.CreateContext();
        var loaded = await context.Bookings.Include(b => b.Slots).SingleAsync(b => b.Id == booking.Id);
        Assert.Equal(Berlin(10, 0), loaded.StartUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.StartUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
        Assert.Equal([Berlin(10, 0), Berlin(10, 15)], loaded.Slots.Select(s => s.SlotStartUtc).Order());
        Assert.All(loaded.Slots, s => Assert.Equal(DateTimeKind.Utc, s.SlotStartUtc.Kind));
    }

    [Fact]
    public async Task Overlapping_booking_is_rejected_by_the_database_and_saves_nothing()
    {
        var resource = await SaveResourceAsync(NewResource());
        var first = Booking.Create(resource, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc);
        // Overlaps only in 10:45; 11:00 and 11:15 are free, but must not be saved either.
        var second = Booking.Create(resource, "user-2", Berlin(10, 45), Berlin(11, 30), NowUtc);
        await SaveBookingAsync(first);

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => SaveBookingAsync(second));

        var slots = await SlotsOfAsync(resource.Id);
        Assert.All(slots, s => Assert.Equal(first.Id, s.BookingId));
        Assert.Equal(4, slots.Count);
        await using var context = _database.CreateContext();
        Assert.False(await context.Bookings.AnyAsync(b => b.Id == second.Id));
    }

    [Fact]
    public async Task Adjacent_bookings_do_not_conflict()
    {
        var resource = await SaveResourceAsync(NewResource());

        await SaveBookingAsync(Booking.Create(resource, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc));
        await SaveBookingAsync(Booking.Create(resource, "user-2", Berlin(11, 0), Berlin(12, 0), NowUtc));

        Assert.Equal(8, (await SlotsOfAsync(resource.Id)).Count);
    }

    [Fact]
    public async Task Same_time_in_different_resources_does_not_conflict()
    {
        var sunflower = await SaveResourceAsync(NewResource("Sunflower"));
        var tulip = await SaveResourceAsync(NewResource("Tulip"));

        await SaveBookingAsync(Booking.Create(sunflower, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc));
        await SaveBookingAsync(Booking.Create(tulip, "user-2", Berlin(10, 0), Berlin(11, 0), NowUtc));

        Assert.Equal(4, (await SlotsOfAsync(sunflower.Id)).Count);
        Assert.Equal(4, (await SlotsOfAsync(tulip.Id)).Count);
    }

    [Fact]
    public async Task Concurrent_edits_of_a_resource_are_detected()
    {
        var resource = await SaveResourceAsync(NewResource());

        // Two admins load the same resource...
        await using var adminA = _database.CreateContext();
        await using var adminB = _database.CreateContext();
        var unitOfWorkA = SqliteTestDatabase.CreateUnitOfWork(adminA);
        var unitOfWorkB = SqliteTestDatabase.CreateUnitOfWork(adminB);
        var copyA = (await unitOfWorkA.Resources.GetByIdAsync(resource.Id))!;
        var copyB = (await unitOfWorkB.Resources.GetByIdAsync(resource.Id))!;

        // ...A saves first, then B tries to save over it.
        copyA.Update("Sunflower (renovated)", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));
        await unitOfWorkA.CompleteAsync();
        copyB.Update("Sunflower", 10, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => unitOfWorkB.CompleteAsync());

        await using var context = _database.CreateContext();
        var stored = await context.Resources.SingleAsync(r => r.Id == resource.Id);
        Assert.Equal("Sunflower (renovated)", stored.Name);
        Assert.Equal(6, stored.Capacity);
    }

    [Fact]
    public async Task Other_database_errors_are_not_reported_as_conflicts()
    {
        // The resource was never saved, so the foreign key fails — a real error,
        // which must not be disguised as "slot already booked".
        var unsavedResource = NewResource();
        var booking = Booking.Create(unsavedResource, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveBookingAsync(booking));

        Assert.IsNotType<DbUpdateConcurrencyException>(error);
    }
}
