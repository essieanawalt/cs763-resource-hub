// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Tests for POST /api/v1/assets/tickets and GET /api/v1/assets/tickets/{id}; reads now send a bearer token (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18
// Confidence: High

using System.Net;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class TicketSubmitTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [DbFact]
    public async Task Instructor_submits_multi_asset_ticket_for_own_course()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", new
        {
            courseId = Ids.CourseCs501,
            projectId = Ids.ProjectDashboard,
            notes = "Team laptops, an IDE seat and cloud credits for the dashboard.",
            items = new object[]
            {
                new { assetId = Ids.AssetLaptop, quantity = 2, loanDays = 10 },
                new { assetId = Ids.AssetJetBrains, quantity = 1 },
                new { assetId = Ids.AssetCloudCredits, amount = 250.00m },
            },
        }, Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.Created);
        var ticket = await response.JsonAsync();
        Assert.Equal($"/api/v1/assets/tickets/{ticket.Id()}", response.Headers.Location?.OriginalString);
        Assert.Equal("pending", ticket.Str("status"));
        Assert.Equal(Ids.InstructorChen, ticket.Id("requesterId"));
        Assert.Equal(Ids.CourseCs501, ticket.Id("courseId"));
        Assert.Equal(Ids.ProjectDashboard, ticket.Id("projectId"));
        Assert.False(ticket.TryGetProperty("researchLabId", out _));

        var items = ticket.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        var laptop = items.Single(i => i.Id("assetId") == Ids.AssetLaptop);
        Assert.Equal(2, laptop.GetProperty("quantity").GetInt32());
        Assert.Equal(10, laptop.GetProperty("loanDays").GetInt32());
        Assert.Equal("physical", laptop.Str("assetType"));
        Assert.Equal("Laptop", laptop.Str("category"));
        var credits = items.Single(i => i.Id("assetId") == Ids.AssetCloudCredits);
        Assert.Equal(250.00m, credits.GetProperty("amount").GetDecimal());

        // Submitting does not touch inventory (spec 6.4).
        var asset = await new Scenario(_client).GetAssetAsync(Ids.AssetLaptop);
        Assert.Equal(5, asset.GetProperty("remainingStock").GetDecimal());
    }

    [DbFact]
    public async Task Advisor_submits_ticket_for_own_lab_and_reads_it_back_with_submitted_event()
    {
        var created = await new Scenario(_client).SubmitAsync(Ids.AdvisorRaman, new
        {
            researchLabId = Ids.LabEmbedded,
            projectId = Ids.ProjectParking,
            items = new[] { new { assetId = Ids.AssetRaspberryPi, quantity = 4 } },
        });

        var response = await _client.GetAsync($"/api/v1/assets/tickets/{created.Id()}", Ids.AdvisorRaman);

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var ticket = await response.JsonAsync();
        Assert.Equal(Ids.LabEmbedded, ticket.Id("researchLabId"));
        Assert.Equal("pending", ticket.Str("status"));
        var ev = Assert.Single(ticket.GetProperty("events").EnumerateArray());
        Assert.Equal("submitted", ev.Str("type"));
        Assert.Equal("pending", ev.Str("toStatus"));
        Assert.Equal(Ids.AdvisorRaman, ev.Id("actorId"));
    }

    [DbFact]
    public async Task Submit_without_user_is_401()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", ValidCourseTicket());
        await response.AssertStatusAsync(HttpStatusCode.Unauthorized);
    }

    [DbTheory]
    [InlineData("40000000-0000-4000-8000-000000000005")] // IT director
    [InlineData("40000000-0000-4000-8000-000000000006")] // administrator
    [InlineData("40000000-0000-4000-8000-000000000002")] // instructor of another course
    [InlineData("40000000-0000-4000-8000-000000000003")] // lab advisor submitting for a course
    public async Task Only_the_courses_own_instructor_may_submit_for_it(string user)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", ValidCourseTicket(), Guid.Parse(user));
        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbFact]
    public async Task Advisor_cannot_submit_for_another_lab()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", new
        {
            researchLabId = Ids.LabEmbedded,
            items = new[] { new { assetId = Ids.AssetRaspberryPi, quantity = 1 } },
        }, Ids.AdvisorFerreira);
        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    // Each body is sent by Dr. Chen (CS501 A1 instructor).
    [DbTheory]
    [InlineData("""{"items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","researchLabId":"20000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001"}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002"}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":0}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002","amount":5}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000004","quantity":5}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000004","amount":-1}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000004","amount":1.001}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000003","quantity":1,"loanDays":7}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1,"loanDays":0}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"59999999-0000-4000-8000-000000000000","quantity":1}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1},{"assetId":"50000000-0000-4000-8000-000000000002","quantity":2}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","projectId":"30000000-0000-4000-8000-000000000002","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","projectId":"39999999-0000-4000-8000-000000000000","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1}]}""")]
    [InlineData("""{"courseId":"10000000-0000-4000-8000-000000000001","status":"approved","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":1}]}""")]
    public async Task Submit_with_invalid_body_is_400(string body)
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", body, Ids.InstructorChen);
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Get_unknown_ticket_is_404()
    {
        var response = await _client.GetAsync($"/api/v1/assets/tickets/{Guid.NewGuid()}", Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.NotFound);
    }

    private static object ValidCourseTicket() => new
    {
        courseId = Ids.CourseCs501,
        items = new[] { new { assetId = Ids.AssetLaptop, quantity = 1 } },
    };
}
