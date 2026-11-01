// AI Contribution Weight: [85%] AI Generated / [15%] Human Refactored
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResourceHub.Tests;

public class HealthEndpointTests
{
    // Port 1 on localhost: nothing listens there, so the connection is refused quickly.
    private const string UnreachableDb = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";

    private static WebApplicationFactory<Program> CreateFactory(
    WebApplicationFactory<Program> baseFactory, string connectionString) =>
    baseFactory.WithWebHostBuilder(builder =>
        builder.UseSetting("ConnectionStrings:HubDb", connectionString));

    [Fact]
    public async Task Health_returns_503_when_database_unreachable()
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = CreateFactory(baseFactory, UnreachableDb);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.Equal(new HealthBody("unhealthy", "unreachable"), body);
    }

    [Fact]
    public async Task Health_does_not_require_user_header()
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = CreateFactory(baseFactory, UnreachableDb);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/health");

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Health_returns_200_when_database_reachable()
    {

        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = CreateFactory(baseFactory, DatabaseFactAttribute.ConnectionString!);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.Equal(new HealthBody("ok", "ok"), body);
    }

    private sealed record HealthBody(string Status, string Database);
}
