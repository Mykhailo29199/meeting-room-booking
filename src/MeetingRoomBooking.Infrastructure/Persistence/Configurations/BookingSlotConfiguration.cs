using MeetingRoomBooking.Domain.Bookings;
using MeetingRoomBooking.Domain.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRoomBooking.Infrastructure.Persistence.Configurations;

internal sealed class BookingSlotConfiguration : IEntityTypeConfiguration<BookingSlot>
{
    public void Configure(EntityTypeBuilder<BookingSlot> builder)
    {
        builder.ToTable("BookingSlots");

        // THE concurrency control of the whole system.
        //
        // The primary key is (ResourceId, SlotStartUtc), so the database itself
        // refuses a second row for the same slot of the same resource. When
        // several requests try to book overlapping times at once, every one of
        // them inserts its slots, the database accepts exactly one, and the
        // others fail with a unique-key violation (SQL Server error 2627) that
        // UnitOfWork turns into UniqueConstraintViolationException -> HTTP 409.
        // There is no "check if free, then insert" step to race against.
        //
        // As a clustered primary key it also keeps a resource's slots
        // physically in time order, which makes the daily schedule query fast
        // and makes concurrent inserts take locks in the same (ascending)
        // order, so they wait for each other instead of deadlocking.
        builder.HasKey(s => new { s.ResourceId, s.SlotStartUtc }).HasName("PK_BookingSlots");

        // No cascade from Resource: the Booking -> Resource relationship already
        // restricts deletion, and SQL Server forbids multiple cascade paths.
        builder.HasOne<Resource>()
            .WithMany()
            .HasForeignKey(s => s.ResourceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(s => s.BookingId);
    }
}
