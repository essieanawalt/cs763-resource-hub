// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Tests for GET /api/v1/health (spec section 2) (~100%)
// Human Modifications: None (review only)
// Verification: dotnet test (failed before HealthEndpoints existed, passes after); reviewed by me 2026-11-15
// Confidence: High

using System.Net;
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
}
