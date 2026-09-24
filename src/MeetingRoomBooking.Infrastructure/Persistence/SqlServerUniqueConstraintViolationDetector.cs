using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Infrastructure.Persistence;

/// <summary>SQL Server / Azure SQL rules for <see cref="IUniqueConstraintViolationDetector"/>.</summary>
public sealed class SqlServerUniqueConstraintViolationDetector : IUniqueConstraintViolationDetector
{
    // 2627: violation of a PRIMARY KEY or UNIQUE constraint (the BookingSlots key).
    // 2601: duplicate key row in a unique index.
    private const int UniqueConstraintViolation = 2627;
    private const int DuplicateKeyInUniqueIndex = 2601;

    public bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueConstraintViolation or DuplicateKeyInUniqueIndex };
}
