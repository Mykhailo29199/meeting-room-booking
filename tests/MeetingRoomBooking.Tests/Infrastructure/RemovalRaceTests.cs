using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Application.Resources;
using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Domain.Bookings;
using MeetingRoomBooking.Domain.Resources;
using MeetingRoomBooking.Tests.Application;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Infrastructure;

/// <summary>
/// A booking and the removal of its resource racing each other must never
/// leave an active booking of a removed resource. Both take the resource-row
/// lock first, so whichever gets it first finishes before the other reads.
///
/// Each scenario holds one side's transaction open at the critical point,
/// starts the other side, checks that it waits, and only then commits — so
/// the interleaving is forced, not left to chance. The SQL Server variants
/// run with READ_COMMITTED_SNAPSHOT (Azure SQL's default), under which
/// removing either lock makes these tests fail.
/// </summary>
public class RemovalRaceTests
{
    private static readonly TimeZoneInfo BerlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly DateTime NowUtc = new(2026, 9, 30, 7, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan GiveItTimeToBlock = TimeSpan.FromMilliseconds(500);

    private static DateTime Berlin(int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 1, hour, minute, 0), BerlinZone);

    [Fact]
    public async Task Removal_waits_for_a_booking_in_flight_and_releases_it_on_Sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await RemovalWaitsForBookingInFlightAndReleasesIt(database);
    }

    [SqlServerFact]
    public async Task Removal_waits_for_a_booking_in_flight_and_releases_it_on_SqlServer()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await RemovalWaitsForBookingInFlightAndReleasesIt(database);
    }

    [Fact]
    public async Task Booking_waits_for_a_removal_in_flight_and_is_rejected_on_Sqlite()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        await BookingWaitsForRemovalInFlightAndIsRejected(database);
    }

    [SqlServerFact]
    public async Task Booking_waits_for_a_removal_in_flight_and_is_rejected_on_SqlServer()
    {
        await using var database = await SqlServerTestDatabase.CreateAsync();
        await BookingWaitsForRemovalInFlightAndIsRejected(database);
    }

    /// <summary>Booking gets the lock first; removal must not miss the booking.</summary>
    private static async Task RemovalWaitsForBookingInFlightAndReleasesIt(ITestDatabase database)
    {
        var resourceId = await SaveResourceAsync(database);

        // The booking's transaction: lock, validate, insert — but not committed yet.
        await using var bookingContext = database.CreateContext();
        var bookingSide = database.NewUnitOfWork(bookingContext);
        await using var bookingTransaction = await bookingSide.BeginTransactionAsync();
        Assert.True(await bookingSide.Resources.LockAsync(resourceId));
        var resource = (await bookingSide.Resources.GetByIdAsync(resourceId))!;
        var booking = Booking.Create(resource, "alice", Berlin(10, 0), Berlin(11, 0), NowUtc);
        bookingSide.Bookings.Add(booking);
        await bookingSide.CompleteAsync();

        var removal = Task.Run(async () =>
        {
            await using var context = database.CreateContext();
            var service = new ResourceService(database.NewUnitOfWork(context), new FixedTimeProvider(NowUtc));
            return await service.RemoveAsync(resourceId);
        });

        await Task.Delay(GiveItTimeToBlock);
        Assert.False(removal.IsCompleted, "The removal should wait for the booking's lock.");

        await bookingTransaction.CommitAsync();
        var result = await removal;

        Assert.Equal(1, result.CancelledBookings);
        await using var verify = database.CreateContext();
        Assert.False(await verify.Bookings.AnyAsync(b => b.Id == booking.Id));
        Assert.False(await verify.BookingSlots.AnyAsync(s => s.ResourceId == resourceId));
    }

    /// <summary>Removal gets the lock first; the booking must see the resource as removed.</summary>
    private static async Task BookingWaitsForRemovalInFlightAndIsRejected(ITestDatabase database)
    {
        var resourceId = await SaveResourceAsync(database);

        // The removal's transaction: lock, deactivate — but not committed yet.
        await using var removalContext = database.CreateContext();
        var removalSide = database.NewUnitOfWork(removalContext);
        await using var removalTransaction = await removalSide.BeginTransactionAsync();
        Assert.True(await removalSide.Resources.LockAsync(resourceId));
        (await removalSide.Resources.GetByIdAsync(resourceId))!.Deactivate();
        await removalSide.CompleteAsync();

        var booking = Task.Run(async () =>
        {
            await using var context = database.CreateContext();
            var service = new BookingService(database.NewUnitOfWork(context), new FixedTimeProvider(NowUtc));
            return await service.CreateAsync(
                new CreateBookingRequest(resourceId, Berlin(10, 0), Berlin(11, 0)), new UserContext("alice", false));
        });

        await Task.Delay(GiveItTimeToBlock);
        Assert.False(booking.IsCompleted, "The booking should wait for the removal's lock.");

        await removalTransaction.CommitAsync();

        await Assert.ThrowsAsync<DomainException>(() => booking);
        await using var verify = database.CreateContext();
        Assert.False(await verify.BookingSlots.AnyAsync(s => s.ResourceId == resourceId));
    }

    private static async Task<Guid> SaveResourceAsync(ITestDatabase database)
    {
        var resource = new Resource("Sunflower", 6, "Europe/Berlin", new TimeOnly(8, 0), new TimeOnly(20, 0));
        await using var context = database.CreateContext();
        var unitOfWork = database.NewUnitOfWork(context);
        unitOfWork.Resources.Add(resource);
        await unitOfWork.CompleteAsync();
        return resource.Id;
    }
}
