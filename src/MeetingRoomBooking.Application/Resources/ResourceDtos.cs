namespace MeetingRoomBooking.Application.Resources;

/// <summary>
/// A resource as the API shows it. <see cref="Version"/> changes on every
/// edit; send it back with an update so a stale edit is rejected instead of
/// overwriting someone else's change.
/// </summary>
public sealed record ResourceDto(
    Guid Id,
    string Name,
    int Capacity,
    string TimeZoneId,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    bool IsActive,
    Guid Version);

/// <param name="TimeZoneId">IANA time zone of the resource, e.g. "Europe/Berlin".</param>
/// <param name="OpensAt">Local opening time, on a 15-minute boundary, e.g. "08:00".</param>
/// <param name="ClosesAt">Local closing time, on a 15-minute boundary, e.g. "20:00".</param>
public sealed record CreateResourceRequest(
    string Name,
    int Capacity,
    string TimeZoneId,
    TimeOnly OpensAt,
    TimeOnly ClosesAt);

/// <param name="Version">The <see cref="ResourceDto.Version"/> the edit is based on.</param>
public sealed record UpdateResourceRequest(
    string Name,
    int Capacity,
    string TimeZoneId,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    Guid Version);

/// <summary>What removing a resource did to its bookings.</summary>
/// <param name="CancelledBookings">Future bookings deleted, their slots freed.</param>
/// <param name="ShortenedBookings">Bookings under way cut short: past slots and the slot in progress kept.</param>
public sealed record ResourceRemovalResult(int CancelledBookings, int ShortenedBookings);
