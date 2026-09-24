using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingRoomBooking.Application.Auth;
using MeetingRoomBooking.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace MeetingRoomBooking.Tests.Api;

public class AuthApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public AuthApiTests(ApiFactory api) => _api = api;

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@test.local";

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return (await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    [Fact]
    public async Task Registration_creates_a_regular_user_and_signs_them_in()
    {
        var result = await _api.RegisterAsync("Anna");

        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.Equal(["User"], result.Roles);

        var me = await _api.CreateClient(result.AccessToken).GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(result.UserId, me.GetProperty("userId").GetString());
        Assert.Equal("Anna", me.GetProperty("displayName").GetString());
        Assert.Equal(["User"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Registration_ignores_any_attempt_to_claim_the_admin_role()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/auth/register", new
        {
            email = UniqueEmail(),
            password = "Passw0rd!",
            displayName = "Mallory",
            roles = new[] { "Admin" }
        });

        var result = await response.Content.ReadFromJsonAsync<AuthResult>();
        Assert.Equal(["User"], result!.Roles);
    }

    [Fact]
    public async Task Weak_password_is_rejected_with_field_errors()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(UniqueEmail(), "short", "Anna"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty("Password", out _));
    }

    [Fact]
    public async Task Email_can_be_registered_only_once()
    {
        var email = UniqueEmail();
        await _api.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Passw0rd!", "Anna"));

        var second = await _api.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Passw0rd!", "Anna 2"));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var problem = await ProblemAsync(second);
        Assert.True(problem.GetProperty("errors").TryGetProperty("Email", out _));
    }

    [Fact]
    public async Task Display_name_is_required()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(UniqueEmail(), "Passw0rd!", "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty("DisplayName", out _));
    }

    [Fact]
    public async Task Login_returns_a_working_token()
    {
        var email = UniqueEmail();
        await _api.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Passw0rd!", "Anna"));

        var response = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Passw0rd!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        var me = await _api.CreateClient(result.AccessToken).GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_401()
    {
        var email = UniqueEmail();
        await _api.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, "Passw0rd!", "Anna"));

        var wrongPassword = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Wr0ng!pass"));
        var unknownEmail = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(UniqueEmail(), "Passw0rd!"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        // Same message: the endpoint must not reveal which emails are registered.
        Assert.Equal(
            (await ProblemAsync(wrongPassword)).GetProperty("detail").GetString(),
            (await ProblemAsync(unknownEmail)).GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Seeded_admin_signs_in_with_the_admin_role()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFactory.AdminEmail, ApiFactory.AdminPassword));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        Assert.Contains("Admin", result.Roles);
    }

    [Fact]
    public async Task Protected_endpoint_without_a_token_is_401()
    {
        var response = await _api.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await ProblemAsync(response);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        // A well-formed token with the right issuer, audience and claims — even
        // the Admin role — but signed with a key the server does not know.
        var result = await _api.RegisterAsync();
        var forger = new JwtTokenService(
            Options.Create(new JwtOptions
            {
                Issuer = "MeetingRoomBooking",
                Audience = "MeetingRoomBooking",
                Key = "an-attacker-chosen-key-that-is-long-enough"
            }),
            TimeProvider.System);
        var (forged, _) = forger.CreateToken(
            new ApplicationUser { Id = result.UserId, Email = result.Email, DisplayName = "Forged" }, ["User", "Admin"]);

        var response = await _api.CreateClient(forged).GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
