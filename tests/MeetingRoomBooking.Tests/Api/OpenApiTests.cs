using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace MeetingRoomBooking.Tests.Api;

public class OpenApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public OpenApiTests(ApiFactory api) => _api = api;

    private async Task<JsonElement> DocumentAsync() =>
        await _api.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");

    private static JsonElement Operation(JsonElement document, string path, string method) =>
        document.GetProperty("paths").GetProperty(path).GetProperty(method);

    [Fact]
    public async Task Document_declares_bearer_token_authentication()
    {
        var scheme = (await DocumentAsync())
            .GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Only_endpoints_that_need_a_token_are_marked_as_secured()
    {
        var document = await DocumentAsync();

        Assert.True(Operation(document, "/api/auth/me", "get").TryGetProperty("security", out _));
        Assert.False(Operation(document, "/api/auth/login", "post").TryGetProperty("security", out _));
        Assert.False(Operation(document, "/api/auth/register", "post").TryGetProperty("security", out _));
    }

    [Fact]
    public async Task Endpoint_descriptions_come_from_the_xml_comments()
    {
        var login = Operation(await DocumentAsync(), "/api/auth/login", "post");

        Assert.Equal("Signs in and returns an access token.", login.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Swagger_ui_is_served()
    {
        var response = await _api.CreateClient().GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Docs_are_served_in_production_too()
    {
        // Decided with the Azure deployment: reviewers try the API there.
        using var production = _api.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var client = production.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }
}
