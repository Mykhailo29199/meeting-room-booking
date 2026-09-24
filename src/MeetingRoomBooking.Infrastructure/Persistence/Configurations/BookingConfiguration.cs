using MeetingRoomBooking.Domain.Bookings;
using MeetingRoomBooking.Domain.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRoomBooking.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        // 450 = the length ASP.NET Core Identity uses for user ids.
        builder.Property(b => b.UserId).HasMaxLength(450).IsRequired();

        // A resource with bookings cannot be deleted by accident; resources are
        // deactivated instead.
        builder.HasOne<Resource>()
            .WithMany()
            .HasForeignKey(b => b.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Slots belong to their booking: cancelling (deleting) a booking frees them.
        builder.HasMany(b => b.Slots)
            .WithOne()
            .HasForeignKey(s => s.BookingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Slots).UsePropertyAccessMode(PropertyAccessMode.Field);

        // "My bookings" and "all bookings of a resource" queries.
        builder.HasIndex(b => b.UserId);
        builder.HasIndex(b => new { b.ResourceId, b.StartUtc });
    }
}
