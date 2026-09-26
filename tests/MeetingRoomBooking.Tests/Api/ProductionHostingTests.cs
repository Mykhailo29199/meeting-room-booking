using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>How the app behaves when deployed (environment Production).</summary>
public sealed class ProductionHostingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public ProductionHostingTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task Https_responses_tell_browsers_to_use_https_only()
    {
        using var production = _api.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        // Not localhost: HSTS is deliberately never sent for it.
        var client = production.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://meetingroombooking.example")
        });

        var response = await client.GetAsync("/api/auth/me");

        Assert.True(response.Headers.TryGetValues("Strict-Transport-Security", out var values));
        Assert.StartsWith("max-age=", Assert.Single(values));
    }
}
