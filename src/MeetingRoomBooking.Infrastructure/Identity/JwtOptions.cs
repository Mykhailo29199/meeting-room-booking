using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MeetingRoomBooking.Infrastructure.Identity;

/// <summary>
/// The "Jwt" configuration section. The same values sign tokens
/// (<see cref="JwtTokenService"/>) and validate them (JwtBearer in the API).
///
/// <see cref="Key"/> is a secret: it lives in the git-ignored
/// appsettings.Development.json locally and in App Service settings in Azure.
/// The app refuses to start without it.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 signing key; at least 32 characters (256 bits).</summary>
    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Range(1, 24 * 60)]
    public int ExpiresMinutes { get; set; } = 60;

    public SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(Key));
}
