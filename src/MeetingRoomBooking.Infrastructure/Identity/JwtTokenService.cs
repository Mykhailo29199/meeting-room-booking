using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MeetingRoomBooking.Infrastructure.Identity;

/// <summary>
/// Issues access tokens: JWTs signed with HMAC-SHA256 (<see cref="JwtOptions.Key"/>).
///
/// Claims use the short JWT names, which the API reads as-is (inbound claim
/// mapping is switched off there):
/// <c>sub</c> = user id (becomes Booking.UserId), <c>email</c>, <c>name</c>
/// = display name, and one <c>role</c> claim per role.
/// No refresh tokens: when the token expires, the user signs in again.
/// </summary>
public sealed class JwtTokenService
{
    public const string RoleClaim = "role";
    public const string NameClaim = "name";

    private readonly JwtOptions _options;
    private readonly TimeProvider _time;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public (string Token, DateTime ExpiresAtUtc) CreateToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var expiresAtUtc = nowUtc.AddMinutes(_options.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(NameClaim, user.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(RoleClaim, role)));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(_options.SigningKey(), SecurityAlgorithms.HmacSha256)
        });

        return (token, expiresAtUtc);
    }
}
