namespace MeetingRoomBooking.Application.Common;

// Outcomes a use case reports to its caller by throwing. This layer knows
// nothing about HTTP; the API maps each exception to one status code (noted
// below for reference). Messages are written for the end user.

/// <summary>
/// The input is invalid, with one or more messages per field (e.g. a weak
/// password). The API returns 400 with the errors listed per field.
/// </summary>
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more fields are invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Wrong email or password. The API returns 401.</summary>
public sealed class AuthenticationFailedException(string message) : Exception(message);

/// <summary>The requested item does not exist. The API returns 404.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The user is not allowed to do this to this item. The API returns 403.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>
/// The request lost a race: the state it relied on changed a moment ago.
/// For bookings: another request took one of the slots first. The API
/// returns 409.
/// </summary>
public sealed class ConflictException(string message, Exception? innerException = null)
    : Exception(message, innerException);
