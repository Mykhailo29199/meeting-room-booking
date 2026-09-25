using System.Reflection;
using MeetingRoomBooking.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>
/// Guards the "secure by default" rule for every controller, present and
/// future: [Authorize] on the class, so an action is protected unless it
/// explicitly opts out with [AllowAnonymous]. Forgetting an attribute on a
/// new action then fails safe (401) instead of exposing it.
/// </summary>
public class ControllerSecurityTests
{
    private static IEnumerable<Type> Controllers() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

    [Fact]
    public void Every_controller_requires_authentication_at_class_level()
    {
        var unprotected = Controllers()
            .Where(c => c.GetCustomAttribute<AuthorizeAttribute>(inherit: true) is null)
            .Select(c => c.Name)
            .ToList();

        Assert.Empty(unprotected);
    }

    [Fact]
    public void Only_sign_up_and_sign_in_are_anonymous()
    {
        // Any new anonymous action must be added here on purpose.
        var anonymous = Controllers()
            .SelectMany(c => c.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .Order()
            .ToList();

        Assert.Equal(["AuthController.Login", "AuthController.Register"], anonymous);
    }
}
