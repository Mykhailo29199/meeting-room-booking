namespace MeetingRoomBooking.Application.Persistence;

/// <summary>
/// Optimistic concurrency: the row being saved was changed by someone else
/// since it was loaded (its version no longer matches), so saving would
/// silently overwrite their change. The API returns it as HTTP 409, asking the
/// user to reload.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(Exception innerException)
        : base("The data was changed by someone else.", innerException) { }
}
