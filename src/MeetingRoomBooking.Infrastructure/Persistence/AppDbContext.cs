using MeetingRoomBooking.Domain.Bookings;
using MeetingRoomBooking.Domain.Resources;
using MeetingRoomBooking.Infrastructure.Identity;
using MeetingRoomBooking.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Infrastructure.Persistence;

/// <summary>
/// One database for everything: resources, bookings and the ASP.NET Core
/// Identity tables (users, roles), so Booking.UserId refers to AspNetUsers.Id.
/// </summary>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingSlot> BookingSlots => Set<BookingSlot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's own tables first; then this app's configurations.
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // The domain works only with UTC; make every DateTime read back from
        // the database say so (databases do not store DateTimeKind).
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ResourceConfiguration.StampNewVersions(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ResourceConfiguration.StampNewVersions(ChangeTracker);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
}
