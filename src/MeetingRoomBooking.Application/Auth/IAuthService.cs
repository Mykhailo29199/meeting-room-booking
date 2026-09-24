namespace MeetingRoomBooking.Application.Auth;

public sealed record RegisterRequest(string Email, string Password, string DisplayName);

public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// A signed-in user: the bearer token to send as
/// <c>Authorization: Bearer &lt;AccessToken&gt;</c>, and who it belongs to.
/// </summary>
public sealed record AuthResult(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string UserId,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);

/// <summary>
/// Accounts and sign-in. Implemented in Infrastructure with ASP.NET Core
/// Identity, so this layer stays free of Identity and token details.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Creates an account with the <see cref="Common.Roles.User"/> role and
    /// signs it in. Registration can never create an admin.
    /// </summary>
    /// <exception cref="Common.ValidationException">Invalid input, weak password or email already taken.</exception>
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <exception cref="Common.AuthenticationFailedException">
    /// Unknown email or wrong password — deliberately the same message for
    /// both, so the endpoint does not reveal which emails are registered.
    /// </exception>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
