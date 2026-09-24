namespace MeetingRoomBooking.Domain;

/// <summary>
/// A business rule was violated — for example a booking outside the room's
/// opening hours. The message is written for the end user; the API returns
/// it with HTTP 400.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
