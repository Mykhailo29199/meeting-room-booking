using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>
/// The published app serves the Angular client from wwwroot next to the API
/// (one origin, no CORS). Here wwwroot is a temporary folder with a stand-in
/// index.html and script, as `ng build` would produce.
/// </summary>
public sealed class ClientHostingTests : IClassFixture<ApiFactory>, IDisposable
{
    private const string IndexMarker = "<app-root>client index</app-root>";
    private const string ScriptContent = "console.log('client');";

    private readonly string _webRoot = Directory.CreateTempSubdirectory("mrb-wwwroot-").FullName;
    private readonly WebApplicationFactory<Program> _app;

    public ClientHostingTests(ApiFactory api)
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), $"<!doctype html><html><body>{IndexMarker}</body></html>");
        File.WriteAllText(Path.Combine(_webRoot, "main-ABC123.js"), ScriptContent);
        _app = api.WithWebHostBuilder(builder => builder.UseWebRoot(_webRoot));
    }

    public void Dispose()
    {
        _app.Dispose();
        Directory.Delete(_webRoot, recursive: true);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/resources")]
    [InlineData("/resources/0198c6a2-0000-7000-8000-000000000001?date=2026-10-01")]
    [InlineData("/admin/bookings?past=true")]
    public async Task Client_routes_get_the_client_page(string path)
    {
        var response = await _app.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(IndexMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Client_files_are_served_as_they_are()
    {
        var response = await _app.CreateClient().GetAsync("/main-ABC123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ScriptContent, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/no-such-endpoint")]
    [InlineData("/api/resources/0198c6a2-0000-7000-8000-000000000001/no-such-thing")]
    [InlineData("/hubs/no-such-hub")]
    public async Task Unknown_api_and_hub_paths_are_404_problem_details_not_the_client_page(string path)
    {
        var response = await _app.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(IndexMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_endpoints_are_not_shadowed_by_the_client()
    {
        // Without a token the real endpoint answers 401, not the client page.
        var response = await _app.CreateClient().GetAsync("/api/resources");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
