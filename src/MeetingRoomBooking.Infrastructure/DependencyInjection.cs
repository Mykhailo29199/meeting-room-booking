using MeetingRoomBooking.Application.Auth;
using MeetingRoomBooking.Application.Persistence;
using MeetingRoomBooking.Infrastructure.Identity;
using MeetingRoomBooking.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeetingRoomBooking.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the database, persistence, accounts and token issuing.
    /// Requires <c>ConnectionStrings:Default</c> and the <c>Jwt</c> section.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:Default is not configured. Locally, copy appsettings.Development.example.json " +
                "to appsettings.Development.json and fill it in.");

        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSingleton<IUniqueConstraintViolationDetector, SqlServerUniqueConstraintViolationDetector>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        // Fail at startup, not at first login, if the Jwt section is missing or weak.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
