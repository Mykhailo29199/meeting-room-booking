using System.Security.Claims;
using MeetingRoomBooking.Application.Common;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MeetingRoomBooking.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated caller as the Application layer sees it. Call only on
    /// endpoints that require authentication.
    /// </summary>
    public static UserContext ToUserContext(this ClaimsPrincipal user)
    {
        var userId = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("The token has no 'sub' claim; is the endpoint missing [Authorize]?");
        return new UserContext(userId, user.IsInRole(Roles.Admin));
    }
}
