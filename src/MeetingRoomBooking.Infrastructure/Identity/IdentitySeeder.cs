using MeetingRoomBooking.Application.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetingRoomBooking.Infrastructure.Identity;

/// <summary>
/// Runs at startup; safe to run every time (creates only what is missing).
///
/// - Always makes sure the roles <see cref="Roles.User"/> and
///   <see cref="Roles.Admin"/> exist.
/// - Creates an admin only if <c>Seed:AdminEmail</c> and
///   <c>Seed:AdminPassword</c> are configured — in the git-ignored
///   appsettings.Development.json locally, in App Service settings in Azure.
///   No default admin credentials exist in the code. This is the only way an
///   admin is created: registration always yields a regular user.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        foreach (var role in new[] { Roles.User, Roles.Admin })
        {
            if (!await roleManager.RoleExistsAsync(role))
                EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)), $"create role {role}");
        }

        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Seed:AdminEmail/Seed:AdminPassword not set; no admin account seeded.");
            return;
        }

        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser { UserName = email, Email = email, DisplayName = "Administrator" };
            EnsureSucceeded(await userManager.CreateAsync(admin, password), "create the seeded admin");
            logger.LogInformation("Seeded admin account {Email}.", email);
        }

        if (!await userManager.IsInRoleAsync(admin, Roles.Admin))
            EnsureSucceeded(await userManager.AddToRoleAsync(admin, Roles.Admin), "grant the Admin role");
    }

    // A failed seed is a configuration error (e.g. the password breaks the
    // password policy): fail startup loudly instead of running without an admin.
    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Identity seeding failed to {action}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
    }
}
