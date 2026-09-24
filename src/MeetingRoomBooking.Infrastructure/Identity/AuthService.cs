using MeetingRoomBooking.Application.Auth;
using MeetingRoomBooking.Application.Common;
using Microsoft.AspNetCore.Identity;

namespace MeetingRoomBooking.Infrastructure.Identity;

/// <summary><see cref="IAuthService"/> on ASP.NET Core Identity.</summary>
internal sealed class AuthService : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password.";

    private readonly UserManager<ApplicationUser> _users;
    private readonly JwtTokenService _tokens;

    public AuthService(UserManager<ApplicationUser> users, JwtTokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length == 0 || displayName.Length > ApplicationUser.MaxDisplayNameLength)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(RegisterRequest.DisplayName)] =
                    [$"Display name is required and must be at most {ApplicationUser.MaxDisplayNameLength} characters."]
            });

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = displayName
        };

        // Identity validates the email (format, uniqueness) and the password policy.
        var created = await _users.CreateAsync(user, request.Password ?? string.Empty);
        if (!created.Succeeded)
            throw new ValidationException(ToFieldErrors(created.Errors));

        // Public registration always yields a regular user — never an admin.
        await _users.AddToRoleAsync(user, Roles.User);

        return await SignInAsync(user);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByEmailAsync(request.Email ?? string.Empty);
        if (user is null || !await _users.CheckPasswordAsync(user, request.Password ?? string.Empty))
            throw new AuthenticationFailedException(InvalidCredentials);

        return await SignInAsync(user);
    }

    private async Task<AuthResult> SignInAsync(ApplicationUser user)
    {
        var roles = (await _users.GetRolesAsync(user)).Order().ToList();
        var (token, expiresAtUtc) = _tokens.CreateToken(user, roles);
        return new AuthResult(token, expiresAtUtc, user.Id, user.Email ?? string.Empty, user.DisplayName, roles);
    }

    /// <summary>Groups Identity's errors by the request field they concern.</summary>
    private static Dictionary<string, string[]> ToFieldErrors(IEnumerable<IdentityError> errors) =>
        errors
            .GroupBy(e => e.Code switch
            {
                _ when e.Code.StartsWith("Password", StringComparison.Ordinal) => nameof(RegisterRequest.Password),
                _ when e.Code.Contains("Email", StringComparison.Ordinal) ||
                       e.Code.Contains("UserName", StringComparison.Ordinal) => nameof(RegisterRequest.Email),
                _ => string.Empty
            })
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
}
