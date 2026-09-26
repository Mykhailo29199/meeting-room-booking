using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRoomBooking.Infrastructure.Persistence;

/// <summary>
/// Applies the EF Core migrations that the database does not have yet,
/// creating the database if it does not exist. Program runs it at startup
/// only where <c>Database:MigrateOnStartup</c> is true (the Azure deployment);
/// elsewhere the schema comes from <c>dotnet ef database update</c>.
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
