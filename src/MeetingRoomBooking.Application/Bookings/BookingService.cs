using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Domain;
using MeetingRoomBooking.Domain.Bookings;

namespace MeetingRoomBooking.Application.Bookings;

/// <summary>
/// Booking use cases: book a time range, cancel a booking, show a resource's
/// schedule for a day. Every committed change to slots is announced to the
/// resource's viewers through <see cref="IScheduleNotifier"/>.
/// </summary>
public sealed class BookingService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;
    private readonly IScheduleNotifier _notifier;

    public BookingService(IUnitOfWork unitOfWork, TimeProvider time, IScheduleNotifier notifier)
    {
        _unitOfWork = unitOfWork;
        _time = time;
        _notifier = notifier;
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
    /// Consistency with removing the resource: before reading the resource,
    /// the transaction takes the resource-row lock
    /// (<see cref="IResourceRepository.LockAsync"/>) that removal takes too.
    /// So the "is it active?" check and the insert happen with no removal in
    /// between: either this booking commits first and the removal then
    /// releases it, or the removal commits first and this request sees the
    /// resource as inactive. There is never an active booking of a removed
    /// resource. The price: bookings of the same resource run one after
    /// another (each holds the lock for milliseconds); different resources do
    /// not wait for each other. The lock is only about removal — which slot
    /// wins is still decided by the primary key.
    /// </summary>
    /// <exception cref="ValidationException">A time is not given in UTC.</exception>
    /// <exception cref="NotFoundException">The resource does not exist.</exception>
    /// <exception cref="DomainException">The request breaks a business rule (e.g. the resource was removed).</exception>
    /// <exception cref="ConflictException">A slot was taken by another request.</exception>
    public async Task<BookingDto> CreateAsync(
        CreateBookingRequest request, UserContext user, CancellationToken cancellationToken = default)
    {
        EnsureUtcInput(request);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        if (!await _unitOfWork.Resources.LockAsync(request.ResourceId, cancellationToken))
            throw new NotFoundException("The resource does not exist.");
        // Read after the lock: this is the resource's current state.
        var resource = (await _unitOfWork.Resources.GetByIdAsync(request.ResourceId, cancellationToken))!;

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

        await transaction.CommitAsync(cancellationToken);

        // After the commit, and only for the winner: a request that lost the
        // race threw above and announces nothing.
        await _notifier.SlotsChangedAsync(booking.ResourceId, SlotChanges.Of(booking.Slots, isBooked: true));
        return ToDto(booking);
    }

    /// <summary>
    /// Releases the part of a booking that has not started yet, so others can
    /// book it (see <see cref="Booking.Release"/>). Before the booking starts
    /// this cancels it completely; while it is under way — "we finished early"
    /// — it frees the remaining slots and keeps the past ones. Users can do
    /// this to their own bookings; admins to any.
    /// </summary>
    /// <exception cref="NotFoundException">The booking does not exist (or was already cancelled).</exception>
    /// <exception cref="ForbiddenException">It is someone else's booking and the user is not an admin.</exception>
    /// <exception cref="DomainException">Nothing is left to release.</exception>
    public async Task<CancellationResult> CancelAsync(Guid bookingId, UserContext user, CancellationToken cancellationToken = default)
    {
        var booking = await _unitOfWork.Bookings.GetByIdAsync(bookingId, cancellationToken)
            ?? throw new NotFoundException("The booking does not exist.");

        if (booking.UserId != user.UserId && !user.IsAdmin)
            throw new ForbiddenException("You can only cancel your own bookings.");

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        // Exactly the slots Release frees: every one that has not started.
        var freed = SlotChanges.Of(booking.Slots.Where(s => s.SlotStartUtc >= nowUtc), isBooked: false);

        var outcome = booking.Release(nowUtc);
        if (outcome == ReleaseOutcome.Cancelled)
        {
            _unitOfWork.Bookings.Remove(booking);
        }
        // For Shortened, the released slots were removed from booking.Slots and
        // are deleted, together with the new EndUtc, in the same commit.

        await _unitOfWork.CompleteAsync(cancellationToken);
        await _notifier.SlotsChangedAsync(booking.ResourceId, freed);
        return outcome == ReleaseOutcome.Cancelled
            ? new CancellationResult(CancelledCompletely: true, RemainingBooking: null)
            : new CancellationResult(CancelledCompletely: false, RemainingBooking: ToDto(booking));
    }

    /// <summary>
    /// Every slot of the resource on its local <paramref name="localDate"/>
    /// (default: today where the resource is), marked free or booked — the
    /// task's "which slots are free and which are booked" view.
    /// </summary>
    /// <exception cref="NotFoundException">
    /// The resource does not exist — or was removed and the user is not an admin.
    /// </exception>
    public async Task<ResourceScheduleDto> GetScheduleAsync(
        Guid resourceId, DateOnly? localDate, UserContext user, CancellationToken cancellationToken = default)
    {
        var resource = await _unitOfWork.Resources.GetByIdAsync(resourceId, cancellationToken);
        if (resource is null || (!resource.IsActive && !user.IsAdmin))
            throw new NotFoundException("The resource does not exist.");

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var date = localDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, resource.TimeZone));

        var slotStarts = resource.GetSlotStarts(date);
        var bookedSlots = slotStarts.Count == 0
            ? []
            : await _unitOfWork.Bookings.GetBookedSlotsAsync(
                resourceId, slotStarts[0], slotStarts[^1] + TimeSlots.Length, cancellationToken);
        var bookedByStart = bookedSlots.ToDictionary(s => s.SlotStartUtc);

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
            resource.Id, resource.Name, resource.TimeZoneId, date, resource.IsActive, slots);
    }

    /// <summary>
    /// Client input, not a programming error: "2026-10-01T08:00:00" (no zone)
    /// or "...+02:00" (converted to server-local time) would otherwise reach
    /// the domain, which accepts only UTC. Rejected with a clear 400 instead
    /// of being guessed at.
    /// </summary>
    private static void EnsureUtcInput(CreateBookingRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.StartUtc.Kind != DateTimeKind.Utc)
            errors[nameof(CreateBookingRequest.StartUtc)] = ["Must be a UTC time ending in 'Z', e.g. 2026-10-01T08:00:00Z."];
        if (request.EndUtc.Kind != DateTimeKind.Utc)
            errors[nameof(CreateBookingRequest.EndUtc)] = ["Must be a UTC time ending in 'Z', e.g. 2026-10-01T09:00:00Z."];
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static BookingDto ToDto(Booking booking) =>
        new(booking.Id, booking.ResourceId, booking.UserId, booking.StartUtc, booking.EndUtc);
}
