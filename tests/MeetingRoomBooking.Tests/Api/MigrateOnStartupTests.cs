using System.Net.Http.Json;
using MeetingRoomBooking.Application.Auth;
using MeetingRoomBooking.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>
/// The Azure deployment sets <c>Database:MigrateOnStartup=true</c> so that a
/// new, empty database gets its schema from the app itself. Needs a real SQL
/// Server: the migrations are SQL Server's. (Everywhere else the setting is
/// off, which every SQLite test relies on: migrating their database, created
/// from the model, would fail at startup.)
/// </summary>
public sealed class MigrateOnStartupTests : IAsyncLifetime
{
    private readonly SqlServerTestDatabase? _database =
        string.IsNullOrWhiteSpace(SqlServerTestDatabase.ServerConnectionString)
            ? null
            : SqlServerTestDatabase.NotCreatedYet();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_database is not null)
            await _database.DisposeAsync();
    }

    [SqlServerFact]
    public async Task With_the_setting_on_the_app_creates_and_migrates_an_empty_database()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", _database!.ConnectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Jwt:Key", $"test-signing-key-{Guid.NewGuid():N}");
            builder.UseSetting("Seed:AdminEmail", ApiFactoryBase.AdminEmail);
            builder.UseSetting("Seed:AdminPassword", ApiFactoryBase.AdminPassword);
        });

        // Startup seeded the admin, so the schema was there before it.
        var response = await app.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(ApiFactoryBase.AdminEmail, ApiFactoryBase.AdminPassword));

        response.EnsureSuccessStatusCode();
        await using var context = _database!.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
    }
}
