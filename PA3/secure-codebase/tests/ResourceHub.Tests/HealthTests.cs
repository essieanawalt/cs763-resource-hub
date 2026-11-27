// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
// AI Contribution: Tests for GET /api/v1/health (spec section 2) and for the --healthcheck probe the Compose health check runs (~100%)
// Human Modifications: None (the 2026-11-15 review covered the earlier version; this change is not reviewed yet)
// Verification: dotnet test (health tests failed before HealthEndpoints existed; probe tests failed to compile before HealthProbe existed; both pass after)
// Confidence: High

using System.Net;
using ResourceHub.Api.Endpoints;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class HealthTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    [DbFact]
    public async Task Health_returns_200_when_database_is_reachable()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/health");

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        Assert.Equal("healthy", body.Str("status"));
    }

    [DbFact]
    public async Task Probe_exits_0_when_the_service_is_healthy()
    {
        Assert.Equal(0, await HealthProbe.CheckAsync(factory.CreateClient(), CancellationToken.None));
    }
}

public class HealthProbeTests
{
    [Theory]
    [InlineData(null, "http://127.0.0.1:8080/api/v1/health")]
    [InlineData("", "http://127.0.0.1:8080/api/v1/health")]
    [InlineData("8080", "http://127.0.0.1:8080/api/v1/health")]
    [InlineData("9090;8081", "http://127.0.0.1:9090/api/v1/health")]
    [InlineData(" 5000 , 5001", "http://127.0.0.1:5000/api/v1/health")]
    [InlineData("not-a-port", "http://127.0.0.1:8080/api/v1/health")]
    [InlineData("70000", "http://127.0.0.1:8080/api/v1/health")]
    public void Probe_targets_the_first_http_port_on_loopback(string? ports, string expected)
    {
        Assert.Equal(new Uri(expected), HealthProbe.UrlFor(ports));
    }

    [Fact]
    public async Task Probe_exits_1_when_nothing_is_listening()
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1"), Timeout = TimeSpan.FromSeconds(3) };
        Assert.Equal(1, await HealthProbe.CheckAsync(client, CancellationToken.None));
    }

    [Fact]
    public async Task Probe_needs_no_configuration_and_sends_no_token()
    {
        // The probe runs before the host is built, so it must not need the database, JWKS or key.
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080") };
        Assert.Equal(0, await HealthProbe.CheckAsync(client, CancellationToken.None));
        Assert.Equal("/api/v1/health", handler.Seen!.RequestUri!.AbsolutePath);
        Assert.Null(handler.Seen.Headers.Authorization);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

public class HealthUnreachableDbTests(UnreachableDbFactory factory) : IClassFixture<UnreachableDbFactory>
{
    [Fact]
    public async Task Health_returns_503_when_database_is_unreachable()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/health");

        await response.AssertStatusAsync(HttpStatusCode.ServiceUnavailable);
        var body = await response.JsonAsync();
        Assert.Equal("unhealthy", body.Str("status"));
    }

    [Fact]
    public async Task Probe_exits_1_when_the_database_is_unreachable()
    {
        Assert.Equal(1, await HealthProbe.CheckAsync(factory.CreateClient(), CancellationToken.None));
    }
}
