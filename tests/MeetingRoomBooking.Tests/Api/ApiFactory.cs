using System.Net.Http.Headers;
using System.Net.Http.Json;
using MeetingRoomBooking.Application.Auth;
using MeetingRoomBooking.Infrastructure.Persistence;
using MeetingRoomBooking.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>
/// The real API — same Program, controllers, authentication and error
/// handling — hosted in memory for HTTP-level tests.
///
/// Differences from production, and only these: the database is SQLite (so
/// the tests run anywhere; see <see cref="SqliteTestDatabase"/>), the JWT key
/// and seeded admin come from test settings, and the "Testing" environment
/// means the developer's appsettings.Development.json is never read.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin#Test1";

    private SqliteTestDatabase _database = null!;

    // The schema must exist before the app starts, because startup seeds roles.
    public async Task InitializeAsync() => _database = await SqliteTestDatabase.CreateAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _database.DisposeAsync();
    }

    internal SqliteTestDatabase Database => _database;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "replaced-by-sqlite-below");
        builder.UseSetting("Jwt:Key", $"test-signing-key-{Guid.NewGuid():N}");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);

        builder.ConfigureTestServices(services =>
        {
            // Swap SQL Server for the test database (EF Core registers both the
            // options and an options-configuration callback; replace both).
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_database.ConnectionString));

            services.RemoveAll<IUniqueConstraintViolationDetector>();
            services.AddSingleton<IUniqueConstraintViolationDetector, SqliteUniqueConstraintViolationDetector>();
        });
    }

    /// <summary>Registers a new user with a unique email and returns their sign-in result.</summary>
    public async Task<AuthResult> RegisterAsync(string displayName = "Test User")
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest($"user-{Guid.NewGuid():N}@test.local", "Passw0rd!", displayName));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResult>())!;
    }

    /// <summary>An HTTP client signed in as the seeded admin.</summary>
    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var response = await CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(AdminEmail, AdminPassword));
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<AuthResult>())!;
        return CreateClient(result.AccessToken);
    }

    /// <summary>An HTTP client signed in as a newly registered regular user.</summary>
    public async Task<HttpClient> CreateUserClientAsync() => CreateClient((await RegisterAsync()).AccessToken);

    /// <summary>An HTTP client that sends <paramref name="accessToken"/> as a Bearer token.</summary>
    public HttpClient CreateClient(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
