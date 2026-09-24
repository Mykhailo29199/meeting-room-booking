using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MeetingRoomBooking.Infrastructure.Persistence;

/// <summary>
/// Stores DateTime values unchanged and marks every value read back as
/// <see cref="DateTimeKind.Utc"/>. Databases do not keep DateTimeKind, and the
/// domain rejects anything that is not explicitly UTC.
/// </summary>
internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(toDatabase => toDatabase, fromDatabase => DateTime.SpecifyKind(fromDatabase, DateTimeKind.Utc)) { }
}
