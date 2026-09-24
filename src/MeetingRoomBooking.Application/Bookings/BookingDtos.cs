namespace MeetingRoomBooking.Application.Bookings;

/// <summary>A request to book a resource. Times are UTC slot boundaries, taken from the schedule.</summary>
public sealed record CreateBookingRequest(Guid ResourceId, DateTime StartUtc, DateTime EndUtc);

public sealed record BookingDto(Guid Id, Guid ResourceId, string UserId, DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// One day of a resource's schedule. The client shows times in
/// <see cref="TimeZoneId"/> and sends back the UTC values unchanged.
/// </summary>
public sealed record ResourceScheduleDto(
    Guid ResourceId,
    string ResourceName,
    string TimeZoneId,
    DateOnly LocalDate,
    bool IsActive,
    IReadOnlyList<SlotDto> Slots);

/// <summary>
/// One 15-minute slot. Who booked someone else's slot is not revealed;
/// <see cref="BookingId"/> is set only for the viewer's own bookings, so they
/// can cancel them.
/// </summary>
public sealed record SlotDto(
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsBooked,
    bool IsMine,
    bool IsPast,
    Guid? BookingId);
