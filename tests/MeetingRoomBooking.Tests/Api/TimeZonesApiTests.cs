using System.Net;
using System.Net.Http.Json;

namespace MeetingRoomBooking.Tests.Api;

public class TimeZonesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public TimeZonesApiTests(ApiFactory api) => _api = api;

    [Fact]
    public async Task Signed_in_users_get_the_time_zones_this_server_accepts()
    {
        var client = await _api.CreateUserClientAsync();

        var zones = await client.GetFromJsonAsync<string[]>("/api/time-zones");

        Assert.NotNull(zones);
        Assert.Contains("Europe/Berlin", zones);
        Assert.Equal(zones.Order(StringComparer.Ordinal), zones);
    }

    [Fact]
    public async Task The_list_needs_a_token()
    {
        var response = await _api.CreateClient().GetAsync("/api/time-zones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
