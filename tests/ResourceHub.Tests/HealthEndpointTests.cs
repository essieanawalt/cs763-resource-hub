// AI Contribution Weight: [100%] AI Generated / [0%] Human Refactored
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ResourceHub.Tests;

public class HealthEndpointTests
{
    // Port 1 on localhost: nothing listens there, so the connection is refused quickly.
    private const string UnreachableDb = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";

    private static HttpClient CreateClient(string connectionString)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:HubDb", connectionString));
        return factory.CreateClient();
    }

    [Fact]
    public async Task Health_returns_503_when_database_unreachable()
    {
        using var client = CreateClient(UnreachableDb);

        var response = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.Equal(new HealthBody("unhealthy", "unreachable"), body);
    }

    [Fact]
    public async Task Health_does_not_require_user_header()
    {
        using var client = CreateClient(UnreachableDb);

        var response = await client.GetAsync("/api/v1/health");

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Health_returns_200_when_database_reachable()
    {
        using var client = CreateClient(DatabaseFactAttribute.ConnectionString!);

        var response = await client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthBody>();
        Assert.Equal(new HealthBody("ok", "ok"), body);
    }

    private sealed record HealthBody(string Status, string Database);
}
