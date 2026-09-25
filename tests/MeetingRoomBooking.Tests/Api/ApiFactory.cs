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
/// Differences from production, and only these: the database is a
/// throw-away test database (see the two subclasses), the JWT key and seeded
/// admin come from test settings, and the "Testing" environment means the
/// developer's appsettings.Development.json is never read.
/// </summary>
public abstract class ApiFactoryBase : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string AdminPassword = "Admin#Test1";

    /// <summary>The database the API runs on, for checking what it actually stored.</summary>
    internal abstract ITestDatabase Database { get; }

    // The schema must exist before the app starts, because startup seeds roles.
    public abstract Task InitializeAsync();

    protected abstract ValueTask DisposeDatabaseAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await DisposeDatabaseAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", $"test-signing-key-{Guid.NewGuid():N}");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
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

/// <summary>
/// The API on SQLite (see <see cref="SqliteTestDatabase"/>): no setup, so
/// every HTTP-level test runs anywhere, including on the reviewer's machine.
/// </summary>
public sealed class ApiFactory : ApiFactoryBase
{
    private SqliteTestDatabase _database = null!;

    internal override ITestDatabase Database => _database;

    public override async Task InitializeAsync() => _database = await SqliteTestDatabase.CreateAsync();

    protected override ValueTask DisposeDatabaseAsync() => _database.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Default", "replaced-by-sqlite-below");

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
}

/// <summary>
/// The API exactly as in production — SQL Server provider, migrations,
/// SQL Server error detection — on a throw-away database with
/// READ_COMMITTED_SNAPSHOT like Azure SQL. Only the connection string differs.
/// Opt-in: when <see cref="SqlServerTestDatabase.ConnectionStringVariable"/>
/// is not set, no database is created and the [SqlServerFact] tests skip.
/// </summary>
public sealed class SqlServerApiFactory : ApiFactoryBase
{
    private SqlServerTestDatabase? _database;

    internal override ITestDatabase Database => _database
        ?? throw new InvalidOperationException("SQL Server tests are not configured.");

    public override async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(SqlServerTestDatabase.ServerConnectionString))
            _database = await SqlServerTestDatabase.CreateAsync();
    }

    protected override async ValueTask DisposeDatabaseAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Default", _database?.ConnectionString ?? "not-configured");
    }
}
