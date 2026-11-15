// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Tests for the events and timing metrics on GET /api/v1/assets/tickets/{id} (spec 6.13) (~100%)
// Human Modifications: None yet
// Verification: dotnet test
// Confidence: High

using System.Net;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class TicketMetricsTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbFact]
    public async Task Ticket_detail_shows_every_event_and_the_time_between_steps()
    {
        var credits = await S.VoucherAsync(100m);
        var ticket = await S.SubmitForCourseAsync(new { assetId = credits, amount = 150m });
        var id = ticket.Id();

        factory.Clock.Advance(TimeSpan.FromHours(3));
        await S.ApproveAsync(id);
        factory.Clock.Advance(TimeSpan.FromHours(1));
        await (await S.AllocateAsync(id)).AssertStatusAsync(HttpStatusCode.Conflict);   // not enough credits
        await (await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{credits}", new
        {
            name = "Test credits", type = "voucher", category = "Test credits", departmentProgram = "Computer Science", balance = 300m,
        }, Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);
        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        await (await S.AllocateAsync(id)).AssertStatusAsync(HttpStatusCode.OK);
        factory.Clock.Advance(TimeSpan.FromHours(2));
        await (await S.SetStatusAsync(id, "closed", Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);

        var detail = await S.GetTicketAsync(id);

        Assert.Equal(
            ["submitted", "approved", "allocation_failed", "allocation_succeeded", "closed"],
            detail.GetProperty("events").EnumerateArray().Select(e => e.Str("type")).ToArray());
        var failed = detail.GetProperty("events").EnumerateArray().Single(e => e.Str("type") == "allocation_failed");
        Assert.Contains("Not enough stock", failed.Str("detail"));

        var metrics = detail.GetProperty("metrics");
        Assert.Equal(3 * 3600, metrics.GetProperty("submittedToDecisionSeconds").GetDouble());
        Assert.Equal(1.5 * 3600, metrics.GetProperty("approvedToAllocatedSeconds").GetDouble());
        Assert.Equal(4.5 * 3600, metrics.GetProperty("submittedToAllocatedSeconds").GetDouble());
        Assert.Equal(2 * 3600, metrics.GetProperty("allocatedToClosedSeconds").GetDouble());
        Assert.Equal(2, metrics.GetProperty("allocationAttempts").GetInt32());
        Assert.Equal(1, metrics.GetProperty("failedAllocationAttempts").GetInt32());
    }

    [DbFact]
    public async Task Rejected_ticket_reports_time_to_decision_and_no_allocation_timings()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        factory.Clock.Advance(TimeSpan.FromMinutes(45));
        await (await S.SetStatusAsync(ticket.Id(), "rejected", Ids.ItDirector, "Out of budget")).AssertStatusAsync(HttpStatusCode.OK);

        var metrics = (await S.GetTicketAsync(ticket.Id())).GetProperty("metrics");

        Assert.Equal(45 * 60, metrics.GetProperty("submittedToDecisionSeconds").GetDouble());
        Assert.False(metrics.TryGetProperty("approvedToAllocatedSeconds", out _));
        Assert.Equal(0, metrics.GetProperty("allocationAttempts").GetInt32());
    }

    [DbFact]
    public async Task Ticket_listing_does_not_include_events()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var page = await (await _client.GetAsync($"/api/v1/assets/tickets?courseId={Ids.CourseCs501}&pageSize=100")).JsonAsync();

        var listed = page.GetProperty("items").EnumerateArray().Single(t => t.Id() == ticket.Id());
        Assert.False(listed.TryGetProperty("events", out _));
    }
}
