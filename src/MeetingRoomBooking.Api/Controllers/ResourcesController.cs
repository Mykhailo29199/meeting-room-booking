using MeetingRoomBooking.Api.Auth;
using MeetingRoomBooking.Application.Bookings;
using MeetingRoomBooking.Application.Common;
using MeetingRoomBooking.Application.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRoomBooking.Api.Controllers;

/// <summary>
/// Resources (e.g. meeting rooms). Any signed-in user can list and view them;
/// only admins can create, edit, remove and restore them.
/// </summary>
[ApiController]
[Route("api/resources")]
[Produces("application/json")]
[Authorize]
public sealed class ResourcesController : ControllerBase
{
    private readonly ResourceService _resources;
    private readonly BookingService _bookings;

    public ResourcesController(ResourceService resources, BookingService bookings)
    {
        _resources = resources;
        _bookings = bookings;
    }

    /// <summary>Lists resources: bookable ones for users, all (including removed) for admins.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ResourceDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ResourceDto>> List(CancellationToken cancellationToken) =>
        _resources.ListAsync(User.ToUserContext(), cancellationToken);

    /// <summary>One resource. A removed resource is visible to admins only.</summary>
    /// <response code="404">The resource does not exist (or was removed).</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceDto> Get(Guid id, CancellationToken cancellationToken) =>
        _resources.GetAsync(id, User.ToUserContext(), cancellationToken);

    /// <summary>A resource's slots for one day: free, booked or past.</summary>
    /// <remarks>
    /// Slot times are UTC; show them in the resource's <c>timeZoneId</c>, and
    /// send them back unchanged to book. Your own bookings carry a
    /// <c>bookingId</c> (to cancel); who booked other slots is not shown.
    /// </remarks>
    /// <param name="id">The resource.</param>
    /// <param name="date">Day in the resource's local time, e.g. 2026-10-01. Default: today there.</param>
    /// <response code="404">The resource does not exist (or was removed).</response>
    [HttpGet("{id:guid}/schedule")]
    [ProducesResponseType<ResourceScheduleDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceScheduleDto> Schedule(Guid id, [FromQuery] DateOnly? date, CancellationToken cancellationToken) =>
        _bookings.GetScheduleAsync(id, date, User.ToUserContext(), cancellationToken);

    /// <summary>Creates a resource. Admin only.</summary>
    /// <remarks>Opening hours are local times in the resource's time zone, on 15-minute boundaries.</remarks>
    /// <response code="201">Created.</response>
    /// <response code="400">Invalid name, capacity, time zone or opening hours.</response>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResourceDto>> Create(CreateResourceRequest request, CancellationToken cancellationToken)
    {
        var created = await _resources.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Edits a resource. Admin only.</summary>
    /// <remarks>
    /// Send the <c>version</c> you loaded. If someone else changed the resource
    /// since, the edit is rejected with 409 instead of overwriting their change.
    /// </remarks>
    /// <response code="400">Invalid new values.</response>
    /// <response code="404">The resource does not exist.</response>
    /// <response code="409">The resource was changed by someone else meanwhile; reload and retry.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ResourceDto> Update(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken) =>
        _resources.UpdateAsync(id, request, cancellationToken);

    /// <summary>Removes a resource. Admin only.</summary>
    /// <remarks>
    /// The resource is hidden from users and can no longer be booked. Its future
    /// bookings are cancelled and bookings under way are cut short; past bookings
    /// are kept. Use the restore endpoint to bring the resource back.
    /// </remarks>
    /// <response code="200">Removed; the body says how many bookings were affected.</response>
    /// <response code="404">The resource does not exist.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceRemovalResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceRemovalResult> Remove(Guid id, CancellationToken cancellationToken) =>
        _resources.RemoveAsync(id, cancellationToken);

    /// <summary>Brings back a removed resource. Admin only.</summary>
    /// <response code="404">The resource does not exist.</response>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceDto> Restore(Guid id, CancellationToken cancellationToken) =>
        _resources.RestoreAsync(id, cancellationToken);
}
