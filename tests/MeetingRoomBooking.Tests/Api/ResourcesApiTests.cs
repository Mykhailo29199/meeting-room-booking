using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MeetingRoomBooking.Application.Resources;

namespace MeetingRoomBooking.Tests.Api;

/// <summary>
/// The resources endpoints over HTTP: who may do what, status codes, and the
/// request/response format. The business rules themselves are covered in
/// ResourceServiceTests.
/// </summary>
public class ResourcesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public ResourcesApiTests(ApiFactory api) => _api = api;

    // Opening hours as a client would send them: "08:00", not "08:00:00".
    private static object NewResourceJson(string name = "Sunflower", string timeZoneId = "Europe/Berlin") =>
        new { name, capacity = 6, timeZoneId, opensAt = "08:00", closesAt = "20:00" };

    private async Task<ResourceDto> CreateAsAdminAsync(string name = "Sunflower")
    {
        var admin = await _api.CreateAdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/resources", NewResourceJson(name));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResourceDto>())!;
    }

    [Fact]
    public async Task Admin_creates_a_resource()
    {
        var admin = await _api.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/resources", NewResourceJson("Tulip"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ResourceDto>())!;
        Assert.Equal($"/api/resources/{created.Id}", response.Headers.Location?.AbsolutePath);
        Assert.Equal("Tulip", created.Name);
        Assert.Equal(new TimeOnly(8, 0), created.OpensAt);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Invalid_resource_is_a_400_with_the_reason()
    {
        var admin = await _api.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/resources", NewResourceJson(timeZoneId: "Nowhere/Atlantis"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("time zone", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Signed_in_user_can_list_and_view_resources()
    {
        var resource = await CreateAsAdminAsync();
        var user = await _api.CreateUserClientAsync();

        var list = await user.GetFromJsonAsync<List<ResourceDto>>("/api/resources");
        var one = await user.GetFromJsonAsync<ResourceDto>($"/api/resources/{resource.Id}");

        Assert.Contains(list!, r => r.Id == resource.Id);
        Assert.Equal(resource.Name, one!.Name);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var response = await _api.CreateClient().GetAsync("/api/resources");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Regular_user_cannot_manage_resources()
    {
        var resource = await CreateAsAdminAsync();
        var user = await _api.CreateUserClientAsync();
        var update = new { name = "Hacked", capacity = 1, timeZoneId = "Europe/Berlin", opensAt = "08:00", closesAt = "20:00", version = resource.Version };

        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync("/api/resources", NewResourceJson())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PutAsJsonAsync($"/api/resources/{resource.Id}", update)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/resources/{resource.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"/api/resources/{resource.Id}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Edit_based_on_a_stale_version_is_a_409()
    {
        var resource = await CreateAsAdminAsync();
        var admin = await _api.CreateAdminClientAsync();
        object Update(string name, Guid version) =>
            new { name, capacity = 6, timeZoneId = "Europe/Berlin", opensAt = "08:00", closesAt = "20:00", version };

        var first = await admin.PutAsJsonAsync($"/api/resources/{resource.Id}", Update("First edit", resource.Version));
        var second = await admin.PutAsJsonAsync($"/api/resources/{resource.Id}", Update("Second edit", resource.Version));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("changed by someone else", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Removed_resource_disappears_for_users_and_comes_back_after_restore()
    {
        var resource = await CreateAsAdminAsync();
        var admin = await _api.CreateAdminClientAsync();
        var user = await _api.CreateUserClientAsync();

        var removal = await admin.DeleteAsync($"/api/resources/{resource.Id}");
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        Assert.Equal(new ResourceRemovalResult(0, 0), await removal.Content.ReadFromJsonAsync<ResourceRemovalResult>());
        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/resources/{resource.Id}")).StatusCode);
        Assert.DoesNotContain((await user.GetFromJsonAsync<List<ResourceDto>>("/api/resources"))!, r => r.Id == resource.Id);
        Assert.False((await admin.GetFromJsonAsync<ResourceDto>($"/api/resources/{resource.Id}"))!.IsActive);

        var restore = await admin.PostAsync($"/api/resources/{resource.Id}/restore", null);

        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/resources/{resource.Id}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_resource_is_a_404()
    {
        var admin = await _api.CreateAdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/resources/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/resources/{Guid.NewGuid()}")).StatusCode);
    }
}
