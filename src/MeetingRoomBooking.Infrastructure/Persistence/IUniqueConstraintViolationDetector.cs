using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Infrastructure.Persistence;

/// <summary>
/// Recognises the database-specific error for a unique-constraint violation.
///
/// This decides what the user sees when a commit fails, so it must be exact:
/// a unique violation means "slot already taken" (409); anything else is a real
/// error and must not be disguised as a conflict. Kept separate so the tests,
/// which run on SQLite, register the SQLite rules without subclassing
/// production code.
/// </summary>
public interface IUniqueConstraintViolationDetector
{
    bool IsUniqueConstraintViolation(DbUpdateException exception);
}
