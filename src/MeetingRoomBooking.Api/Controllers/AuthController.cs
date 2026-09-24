using System.Security.Claims;
using MeetingRoomBooking.Application.Auth;
using MeetingRoomBooking.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MeetingRoomBooking.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Creates an account (role User) and signs it in.</summary>
    /// <response code="200">Registered; use <c>accessToken</c> as a Bearer token.</response>
    /// <response code="400">Invalid input, weak password or email already taken.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<AuthResult> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        _auth.RegisterAsync(request, cancellationToken);

    /// <summary>Signs in and returns an access token.</summary>
    /// <response code="200">Signed in; use <c>accessToken</c> as a Bearer token.</response>
    /// <response code="401">Invalid email or password.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        _auth.LoginAsync(request, cancellationToken);

    /// <summary>Who the current token belongs to — handy for checking a token.</summary>
    /// <response code="401">Missing, invalid or expired token.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public CurrentUserDto Me() => new(
        User.FindFirstValue(JwtRegisteredClaimNames.Sub)!,
        User.FindFirstValue(JwtRegisteredClaimNames.Email)!,
        User.FindFirstValue(JwtTokenService.NameClaim)!,
        User.FindAll(JwtTokenService.RoleClaim).Select(c => c.Value).Order().ToList());

    public sealed record CurrentUserDto(string UserId, string Email, string DisplayName, IReadOnlyList<string> Roles);
}
