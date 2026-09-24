using MeetingRoomBooking.Domain.Resources;

namespace MeetingRoomBooking.Domain.Bookings;

/// <summary>
/// A user's booking of a resource from <see cref="StartUtc"/> to
/// <see cref="EndUtc"/>, stored as one <see cref="BookingSlot"/> per 15
/// minutes. The shortest booking is a single 15-minute slot.
///
/// Created only through <see cref="Create"/>, which enforces every business
/// rule. Whether the slots are still free is deliberately NOT checked here:
/// that is decided by the database's unique (ResourceId, SlotStartUtc)
/// constraint at commit time, the only place it can be decided safely under
/// concurrency.
/// </summary>
public class Booking
{
    private readonly List<BookingSlot> _slots = [];

    public Guid Id { get; private set; }
    public Guid ResourceId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>The booking's slots, in ascending time order.</summary>
    public IReadOnlyCollection<BookingSlot> Slots => _slots;

    // Used by EF Core when loading from the database.
    private Booking() { }

    /// <summary>
    /// Validates the request against the resource's rules and builds the
    /// booking with one slot per 15 minutes.
    /// </summary>
    /// <param name="startUtc">Start of the booking, UTC.</param>
    /// <param name="endUtc">End of the booking (exclusive), UTC.</param>
    /// <param name="nowUtc">Current time, UTC; passed in so the rule is testable.</param>
    /// <exception cref="DomainException">A business rule is violated.</exception>
    /// <exception cref="ArgumentException">A time is not UTC or the user id is missing — a programming error, not user input.</exception>
    public static Booking Create(Resource resource, string userId, DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        EnsureUtc(startUtc, nameof(startUtc));
        EnsureUtc(endUtc, nameof(endUtc));
        EnsureUtc(nowUtc, nameof(nowUtc));

        if (!resource.IsActive)
            throw new DomainException($"'{resource.Name}' is not available for booking.");
        if (endUtc <= startUtc)
            throw new DomainException("A booking must end after it starts.");
        if (!TimeSlots.IsAligned(startUtc) || !TimeSlots.IsAligned(endUtc))
            throw new DomainException("A booking must start and end on a 15-minute boundary, e.g. 10:00 or 10:15.");
        if (startUtc < nowUtc)
            throw new DomainException("A booking cannot start in the past.");
        if (!resource.IsOpenDuring(startUtc, endUtc))
            throw new DomainException(
                $"'{resource.Name}' can only be booked between {resource.OpensAt:HH\\:mm} and " +
                $"{resource.ClosesAt:HH\\:mm} ({resource.TimeZoneId} time), within a single day.");

        var booking = new Booking
        {
            Id = Guid.CreateVersion7(),
            ResourceId = resource.Id,
            UserId = userId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            CreatedAtUtc = nowUtc
        };

        // Ascending order matters later: concurrent transactions that insert
        // overlapping slots in the same order queue up on the first shared slot
        // instead of deadlocking on each other.
        for (var slot = startUtc; slot < endUtc; slot += TimeSlots.Length)
        {
            booking._slots.Add(new BookingSlot(booking.Id, resource.Id, slot));
        }

        return booking;
    }

    private static void EnsureUtc(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Expected a UTC time (DateTimeKind.Utc).", paramName);
    }
}
