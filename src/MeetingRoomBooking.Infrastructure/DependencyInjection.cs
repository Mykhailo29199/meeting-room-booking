using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeetingRoomBooking.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the database and persistence services. Requires <c>ConnectionStrings:Default</c>.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Locally, copy appsettings.Development.example.json " +
                "to appsettings.Development.json and fill it in.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSingleton<IUniqueConstraintViolationDetector, SqlServerUniqueConstraintViolationDetector>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
