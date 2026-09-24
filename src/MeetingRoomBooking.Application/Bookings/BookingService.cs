using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Domain.Bookings;

namespace MeetingRoomBooking.Application.Bookings;

/// <summary>
/// Booking use cases: book a time range, cancel a booking, show a resource's
/// schedule for a day.
/// </summary>
public sealed class BookingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public BookingService(IUnitOfWork unitOfWork, TimeProvider time)
    {
        _unitOfWork = unitOfWork;
        _time = time;
    }

    /// <summary>
    /// Books [StartUtc, EndUtc) of a resource for <paramref name="user"/>.
    ///
    /// How concurrency is handled: there is NO "are these slots free?" query.
    /// The booking and its slots are inserted in one transaction, and the
    /// database's primary key on BookingSlots (ResourceId, SlotStartUtc)
    /// accepts it only if every slot is still free. Of several simultaneous
    /// requests for the same slot, exactly one transaction succeeds; each of
    /// the others gets <see cref="ConflictException"/>, which the API returns
    /// as HTTP 409 — never an overwrite and never a server error.
    ///
    /// The resource is loaded to validate the request (exists, active, open at
    /// that time). If an admin deactivates it at the same moment, the booking
    /// may still commit — which is fine: deactivation keeps existing bookings,
    /// so the result equals booking a second before deactivation.
    /// </summary>
    /// <exception cref="NotFoundException">The resource does not exist.</exception>
    /// <exception cref="DomainException">The request breaks a business rule.</exception>
    /// <exception cref="ConflictException">A slot was taken by another request.</exception>
    public async Task<BookingDto> CreateAsync(
        CreateBookingRequest request, UserContext user, CancellationToken cancellationToken = default)
    {
        var resource = await _unitOfWork.Resources.GetByIdAsync(request.ResourceId, cancellationToken)
            ?? throw new NotFoundException("The resource does not exist.");

        var booking = Booking.Create(
            resource, user.UserId, request.StartUtc, request.EndUtc, _time.GetUtcNow().UtcDateTime);
        _unitOfWork.Bookings.Add(booking);

        try
        {
            await _unitOfWork.CompleteAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex)
        {
            throw new ConflictException(
                "Sorry, someone else has just booked this time. Please pick another slot.", ex);
        }

        return ToDto(booking);
    }

    /// <summary>
    /// Releases the part of a booking that has not started yet, so others can
    /// book it (see <see cref="Booking.Release"/>). Before the booking starts
    /// this cancels it completely; while it is under way — "we finished early"
    /// — it frees the remaining slots and keeps the past ones. Users can do
    /// this to their own bookings; admins to any.
    /// </summary>
    /// <returns>The shortened booking, or null if it was cancelled completely.</returns>
    /// <exception cref="NotFoundException">The booking does not exist (or was already cancelled).</exception>
    /// <exception cref="ForbiddenException">It is someone else's booking and the user is not an admin.</exception>
    /// <exception cref="DomainException">Nothing is left to release.</exception>
    public async Task<BookingDto?> CancelAsync(Guid bookingId, UserContext user, CancellationToken cancellationToken = default)
    {
        var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("The booking does not exist.");

        if (booking.UserId != user.UserId && !user.IsAdmin)
            throw new ForbiddenException("You can only cancel your own bookings.");

        var outcome = booking.Release(_time.GetUtcNow().UtcDateTime);
        if (outcome == ReleaseOutcome.Cancelled)
        {
            _unitOfWork.Bookings.Remove(booking);
        }
        // For Shortened, the released slots were removed from booking.Slots and
        // are deleted, together with the new EndUtc, in the same commit.

        await _unitOfWork.CompleteAsync(cancellationToken);
        return outcome == ReleaseOutcome.Cancelled ? null : ToDto(booking);
    }

    /// <summary>
    /// Every slot of the resource on its local <paramref name="localDate"/>,
    /// marked free or booked — the "fixed set of bookable time slots" view.
    /// </summary>
    /// <exception cref="NotFoundException">The resource does not exist.</exception>
    public async Task<ResourceScheduleDto> GetScheduleAsync(
        Guid resourceId, DateOnly localDate, UserContext user, CancellationToken cancellationToken = default)
    {
        var resource = await _unitOfWork.Resources.GetByIdAsync(resourceId, cancellationToken)
            ?? throw new NotFoundException("The resource does not exist.");

        var slotStarts = resource.GetSlotStarts(localDate);
        var bookedSlots = slotStarts.Count == 0
            ? []
            : await _unitOfWork.Bookings.GetBookedSlotsAsync(
                resourceId, slotStarts[0], slotStarts[^1] + TimeSlots.Length, cancellationToken);
        var bookedByStart = bookedSlots.ToDictionary(s => s.SlotStartUtc);
        var nowUtc = _time.GetUtcNow().UtcDateTime;

        var slots = slotStarts.Select(start =>
        {
            var isBooked = bookedByStart.TryGetValue(start, out var booked);
            var isMine = isBooked && booked!.UserId == user.UserId;
            return new SlotDto(
                StartUtc: start,
                EndUtc: start + TimeSlots.Length,
                IsBooked: isBooked,
                IsMine: isMine,
                IsPast: start < nowUtc,
                BookingId: isMine ? booked!.BookingId : null);
        }).ToList();

        return new ResourceScheduleDto(
            resource.Id, resource.Name, resource.TimeZoneId, localDate, resource.IsActive, slots);
    }

    private static BookingDto ToDto(Booking booking) =>
        new(booking.Id, booking.ResourceId, booking.UserId, booking.StartUtc, booking.EndUtc);
}
