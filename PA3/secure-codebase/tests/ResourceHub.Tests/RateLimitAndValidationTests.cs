// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Per-caller rate limiting tests (T3) and extra write-endpoint input validation tests (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18
// Confidence: High

using System.Net;
using Microsoft.AspNetCore.Hosting;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

/// <summary>Same API with a tiny limit so the tests can reach it quickly.</summary>
public sealed class LowRateLimitFactory : HubApiFactory
{
    public const int Limit = 5;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimiting:PermitLimit", Limit.ToString());
        builder.UseSetting("RateLimiting:AnonymousPermitLimit", Limit.ToString());
        builder.UseSetting("RateLimiting:WindowSeconds", "60");
    }
}

public class RateLimitTests(LowRateLimitFactory factory) : IClassFixture<LowRateLimitFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [DbFact]
    public async Task One_caller_flooding_the_api_gets_429_and_others_are_unaffected()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < LowRateLimitFactory.Limit + 3; i++)
        {
            statuses.Add((await _client.GetAsync("/api/v1/assets", Ids.InstructorOkafor)).StatusCode);
        }

        Assert.Equal(LowRateLimitFactory.Limit, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));

        var limited = await _client.GetAsync("/api/v1/assets", Ids.InstructorOkafor);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null);

        // Another caller has their own budget.
        await (await _client.GetAsync("/api/v1/assets", Ids.AdvisorFerreira)).AssertStatusAsync(HttpStatusCode.OK);
    }

    [DbFact]
    public async Task Writes_count_against_the_same_budget()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < LowRateLimitFactory.Limit + 1; i++)
        {
            statuses.Add((await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", new
            {
                courseId = Ids.CourseCs501,
                items = new[] { new { assetId = Ids.AssetLaptop, quantity = 1 } },
            }, Ids.InstructorChen)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal(LowRateLimitFactory.Limit, statuses.Count(s => s == HttpStatusCode.Created));
    }

    [DbFact]
    public async Task Unauthenticated_floods_are_limited_too()
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < LowRateLimitFactory.Limit + 2; i++)
        {
            statuses.Add((await _client.SendAsync(HttpMethod.Get, "/api/v1/assets", token: "garbage")).StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.DoesNotContain(HttpStatusCode.OK, statuses);
    }
}

public class WriteValidationTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbTheory]
    [InlineData("""{"token":"","checkInCode":"CI-1"}""")]
    [InlineData("""{"token":"HW-1"}""")]
    [InlineData("""{"token":"HW-1","checkInCode":""}""")]
    [InlineData("""{"token":"HW-1234567890123456789012345678901234567890X","checkInCode":"CI-1"}""")]
    [InlineData("""{"token":"HW-1","checkInCode":"CI-1234567890123456789012345678901234567890X"}""")]
    [InlineData("""{"token":"HW 1; DROP TABLE","checkInCode":"CI-1"}""")]
    [InlineData("""{"token":12,"checkInCode":"CI-1"}""")]
    [InlineData("""not json""")]
    public async Task Return_with_invalid_body_is_400(string body)
    {
        var asset = await S.PhysicalAsync(units: 1);
        var response = await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{asset}/return", body, Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Status_change_with_an_overlong_reason_is_400()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        var response = await S.SetStatusAsync(ticket.Id(), "approved", Ids.Admin, new string('x', 501));
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbTheory]
    [InlineData("""{"status":"lost"}""")]
    [InlineData("""{"status":3}""")]
    [InlineData("""{"courseId":"not-a-guid","items":[]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","notes":"__LONG__","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1001}]}""")]
    public async Task Ticket_update_with_invalid_body_is_400(string body)
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{ticket.Id()}",
            body.Replace("__LONG__", new string('n', 2001)), Ids.InstructorChen);
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Too_many_items_is_400()
    {
        var items = Enumerable.Range(0, 21).Select(_ => new { assetId = Guid.NewGuid(), quantity = 1 }).ToArray();
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", new { courseId = Ids.CourseCs501, items }, Ids.InstructorChen);
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Asset_name_with_control_characters_is_400()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", new
        {
            name = "Bad\u0000name",
            type = "voucher",
            category = "Cloud",
            departmentProgram = "CS",
            balance = 10,
        }, Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }
}
