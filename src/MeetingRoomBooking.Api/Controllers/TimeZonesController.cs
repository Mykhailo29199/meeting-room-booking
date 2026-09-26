using MeetingRoomBooking.Application.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRoomBooking.Api.Controllers;

/// <summary>Time zones a resource can have, as this server knows them.</summary>
[ApiController]
[Route("api/time-zones")]
[Produces("application/json")]
[Authorize]
public sealed class TimeZonesController : ControllerBase
{
    private readonly ResourceService _resources;

    public TimeZonesController(ResourceService resources) => _resources = resources;

    /// <summary>The IANA time zone ids a resource can have on this server, sorted.</summary>
    /// <remarks>
    /// Offer these rather than the browser's own list: names differ between
    /// systems (browsers list "Europe/Kiev", this server may know only
    /// "Europe/Kyiv"), and only these are accepted.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public IReadOnlyList<string> List() => _resources.ListTimeZones();
}
