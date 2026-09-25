using MeetingRoomBooking.Api.Auth;
using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRoomBooking.Api.Controllers;

/// <summary>
/// Booking time on resources. To find free slots first, use
/// GET /api/resources/{id}/schedule and send back the slot times it returns.
/// </summary>
[ApiController]
[Route("api/bookings")]
[Produces("application/json")]
[Authorize]
public sealed class BookingsController : ControllerBase
{
    private readonly BookingService _bookings;
    private readonly BookingListService _lists;

    public BookingsController(BookingService bookings, BookingListService lists)
    {
        _bookings = bookings;
        _lists = lists;
    }

    /// <summary>Books a time range of a resource for the signed-in user.</summary>
    /// <remarks>
    /// <c>startUtc</c> and <c>endUtc</c> are UTC slot boundaries, as returned by
    /// the schedule endpoint; one booking is one or more consecutive 15-minute
    /// slots. If several people book overlapping times at the same moment,
    /// exactly one succeeds and the others get 409.
    /// </remarks>
    /// <response code="201">Booked.</response>
    /// <response code="400">Breaks a rule: outside opening hours, in the past, not on the 15-minute grid, resource removed…</response>
    /// <response code="404">The resource does not exist.</response>
    /// <response code="409">Someone else has just booked (part of) this time.</response>
    [HttpPost]
    [ProducesResponseType<BookingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.CreateAsync(request, User.ToUserContext(), cancellationToken);
        return Created($"/api/bookings/{booking.Id}", booking);
    }

    /// <summary>Cancels a booking, or ends it early if it is under way.</summary>
    /// <remarks>
    /// Frees every slot that has not started yet. Before the booking starts it is
    /// cancelled completely; while it is under way, the past and the current slot
    /// stay and the rest is freed. Users cancel their own bookings; admins any.
    /// </remarks>
    /// <response code="400">Nothing left to free (the booking is over or in its last slot).</response>
    /// <response code="403">It is someone else's booking.</response>
    /// <response code="404">The booking does not exist.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<CancellationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CancellationResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        _bookings.CancelAsync(id, User.ToUserContext(), cancellationToken);

    /// <summary>The signed-in user's bookings, earliest first.</summary>
    /// <param name="includePast">Also list bookings that have already ended.</param>
    [HttpGet("mine")]
    [ProducesResponseType<IReadOnlyList<BookingListItem>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<BookingListItem>> Mine([FromQuery] bool includePast, CancellationToken cancellationToken) =>
        _lists.ListMineAsync(User.ToUserContext(), includePast, cancellationToken);

    /// <summary>All users' bookings, earliest first. Admin only.</summary>
    /// <param name="resourceId">Only this resource's bookings.</param>
    /// <param name="includePast">Also list bookings that have already ended.</param>
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<IReadOnlyList<BookingListItem>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<BookingListItem>> All(
        [FromQuery] Guid? resourceId, [FromQuery] bool includePast, CancellationToken cancellationToken) =>
        _lists.ListAllAsync(resourceId, includePast, cancellationToken);
}
