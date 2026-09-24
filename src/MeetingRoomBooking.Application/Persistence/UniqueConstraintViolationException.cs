namespace MeetingRoomBooking.Application.Persistence;

/// <summary>
/// The database rejected a commit because a row would violate a unique
/// constraint. For bookings this means another request took one of the slots
/// first — the expected outcome for every losing request in a race. Services
/// translate it into <see cref="Common.ConflictException"/>, which the API
/// returns as HTTP 409, never as a server error.
/// </summary>
public class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(Exception innerException)
        : base("The change conflicts with existing data.", innerException) { }
}
