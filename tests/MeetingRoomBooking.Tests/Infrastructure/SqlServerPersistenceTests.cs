using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain.Bookings;
using MeetingRoomBooking.Domain.Resources;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Infrastructure;

/// <summary>
/// The persistence guarantees that depend on SQL Server specifically: its
/// error numbers for a duplicate key (recognised by
/// SqlServerUniqueConstraintViolationDetector) and the schema created by the
/// migrations. The SQLite tests cannot prove either. Opt-in — see
/// <see cref="SqlServerTestDatabase"/>.
/// </summary>
public class SqlServerPersistenceTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo BerlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateTime NowUtc = new(2026, 9, 30, 7, 0, 0, DateTimeKind.Utc);

    private SqlServerTestDatabase? _database;

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(SqlServerTestDatabase.ServerConnectionString))
        {
            _database = await SqlServerTestDatabase.CreateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private SqlServerTestDatabase Database => _database!;

    private static DateTime Berlin(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 1).AddHours(hour).AddMinutes(minute), BerlinZone);

    private static Resource NewResource() =>
        new("Sunflower", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));

    private async Task SaveAsync(Action<IUnitOfWork> stage)
    {
        await using var context = Database.CreateContext();
        var unitOfWork = SqlServerTestDatabase.CreateUnitOfWork(context);
        stage(unitOfWork);
        await unitOfWork.CompleteAsync();
    }

    [SqlServerFact]
    public async Task Overlapping_booking_is_reported_as_a_unique_constraint_violation()
    {
        var resource = NewResource();
        await SaveAsync(u => u.Resources.Add(resource));
        await SaveAsync(u => u.Bookings.Add(Booking.Create(resource, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc)));
        var overlapping = Booking.Create(resource, "user-2", Berlin(10, 45), Berlin(11, 30), NowUtc);

        await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => SaveAsync(u => u.Bookings.Add(overlapping)));

        await using var context = Database.CreateContext();
        Assert.Equal(4, await context.BookingSlots.CountAsync(s => s.ResourceId == resource.Id));
        Assert.False(await context.Bookings.AnyAsync(b => b.Id == overlapping.Id));
    }

    [SqlServerFact]
    public async Task Foreign_key_violation_is_not_reported_as_a_conflict()
    {
        var unsavedResource = NewResource();
        var booking = Booking.Create(unsavedResource, "user-1", Berlin(10, 0), Berlin(11, 0), NowUtc);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(u => u.Bookings.Add(booking)));

        Assert.IsNotType<DbUpdateConcurrencyException>(error);
    }

    [SqlServerFact]
    public async Task Concurrent_edits_of_a_resource_are_detected()
    {
        var resource = NewResource();
        await SaveAsync(u => u.Resources.Add(resource));

        await using var adminA = Database.CreateContext();
        await using var adminB = Database.CreateContext();
        var unitOfWorkA = SqlServerTestDatabase.CreateUnitOfWork(adminA);
        var unitOfWorkB = SqlServerTestDatabase.CreateUnitOfWork(adminB);
        var copyA = (await unitOfWorkA.Resources.GetByIdAsync(resource.Id))!;
        var copyB = (await unitOfWorkB.Resources.GetByIdAsync(resource.Id))!;

        copyA.Update("Sunflower (renovated)", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));
        await unitOfWorkA.CompleteAsync();
        copyB.Update("Sunflower", 10, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => unitOfWorkB.CompleteAsync());
    }
}
