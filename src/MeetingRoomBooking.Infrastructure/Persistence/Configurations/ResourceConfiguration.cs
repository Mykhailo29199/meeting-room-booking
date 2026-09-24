using MeetingRoomBooking.Domain.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRoomBooking.Infrastructure.Persistence.Configurations;

internal sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    /// <summary>
    /// Shadow property used as the optimistic-concurrency token.
    ///
    /// Two admins editing the same resource must not silently overwrite each
    /// other. Every save stamps a new Version, and EF Core only updates the
    /// row if the Version in the database is still the one that was loaded —
    /// otherwise the save fails with DbUpdateConcurrencyException, surfaced as
    /// ConcurrencyConflictException.
    ///
    /// An application-generated Guid is used instead of SQL Server's
    /// <c>rowversion</c>: the technique is the same (optimistic versioning),
    /// but it behaves identically on SQL Server and on SQLite, which the
    /// tests run against. The domain model knows nothing about it.
    /// </summary>
    public const string VersionProperty = "Version";

    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("Resources");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Name).HasMaxLength(Resource.MaxNameLength).IsRequired();
        builder.Property(r => r.TimeZoneId).HasMaxLength(64).IsRequired();

        // Derived from TimeZoneId, not stored.
        builder.Ignore(r => r.TimeZone);

        builder.Property<Guid>(VersionProperty).IsConcurrencyToken();
    }

    /// <summary>Gives every added or modified resource a new version before it is saved.</summary>
    internal static void StampNewVersions(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries<Resource>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(VersionProperty).CurrentValue = Guid.NewGuid();
            }
        }
    }
}
