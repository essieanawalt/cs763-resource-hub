// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Tests for PUT/DELETE on /api/v1/assets/tickets/{id}: edits, sign-off transitions, deletes (~100%)
// Human Modifications: None (review only)
// Verification: dotnet test (failed before PUT/DELETE existed, passes after); reviewed by me 2026-11-15, tests for making edits by requestor here, but no tests anywhere that would have hinted at the spoof loophole 
// Confidence: High

using System.Net;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class TicketLifecycleTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbFact]
    public async Task Requester_edits_pending_ticket_content()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{ticket.Id()}", new
        {
            courseId = Ids.CourseCs501,
            projectId = Ids.ProjectDashboard,
            notes = "Changed my mind: credits instead.",
            items = new[] { new { assetId = Ids.AssetCloudCredits, amount = 100.25m } },
        }, Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var updated = await response.JsonAsync();
        Assert.Equal("pending", updated.Str("status"));
        Assert.Equal(Ids.ProjectDashboard, updated.Id("projectId"));
        var item = Assert.Single(updated.GetProperty("items").EnumerateArray());
        Assert.Equal(Ids.AssetCloudCredits, item.Id("assetId"));
        Assert.Equal(100.25m, item.GetProperty("amount").GetDecimal());

        var detail = await S.GetTicketAsync(ticket.Id());
        Assert.Contains(detail.GetProperty("events").EnumerateArray(), e => e.Str("type") == "updated");
    }

    [DbTheory]
    [InlineData("40000000-0000-4000-8000-000000000002")] // another instructor
    [InlineData("40000000-0000-4000-8000-000000000005")] // IT director
    public async Task Only_the_requester_edits_content(string user)
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{ticket.Id()}", new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 2 } },
        }, Guid.Parse(user));

        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbFact]
    public async Task Content_cannot_change_after_approval()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        await S.ApproveAsync(ticket.Id());

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{ticket.Id()}", new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 3 } },
        }, Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.Conflict);
    }

    [DbFact]
    public async Task It_director_approves_pending_ticket_and_inventory_is_unchanged()
    {
        var asset = await S.PhysicalAsync(units: 5);
        var ticket = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 2 });
        factory.Clock.Advance(TimeSpan.FromHours(2));

        var response = await S.SetStatusAsync(ticket.Id(), "approved", Ids.ItDirector, "Budget confirmed");

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var approved = await response.JsonAsync();
        Assert.Equal("approved", approved.Str("status"));
        Assert.True(approved.TryGetProperty("approvedAt", out _));

        // Sign-off does not change inventory (requirement 4); the units show as reserved.
        var after = await S.GetAssetAsync(asset);
        Assert.Equal(5, after.GetProperty("remainingStock").GetDecimal());
        Assert.Equal(2, after.GetProperty("availability").GetProperty("reserved").GetInt32());

        var reserved = await (await _client.GetAsync("/api/v1/assets?availability=reserved&pageSize=100")).JsonAsync();
        Assert.Contains(reserved.GetProperty("items").EnumerateArray(), a => a.Id() == asset);

        var detail = await S.GetTicketAsync(ticket.Id());
        var ev = detail.GetProperty("events").EnumerateArray().Single(e => e.Str("type") == "approved");
        Assert.Equal("pending", ev.Str("fromStatus"));
        Assert.Equal("approved", ev.Str("toStatus"));
        Assert.Equal(Ids.ItDirector, ev.Id("actorId"));
        Assert.Equal("Budget confirmed", ev.Str("detail"));
    }

    [DbFact]
    public async Task It_director_rejects_and_rejected_ticket_cannot_be_approved()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var rejected = await S.SetStatusAsync(ticket.Id(), "rejected", Ids.ItDirector, "Not this term");
        await rejected.AssertStatusAsync(HttpStatusCode.OK);
        Assert.Equal("rejected", (await rejected.JsonAsync()).Str("status"));

        var approve = await S.SetStatusAsync(ticket.Id(), "approved", Ids.ItDirector);
        await approve.AssertStatusAsync(HttpStatusCode.Conflict);
    }

    [DbTheory]
    [InlineData("40000000-0000-4000-8000-000000000001")] // the requester
    [InlineData("40000000-0000-4000-8000-000000000006")] // administrator
    public async Task Only_the_it_director_signs_off(string user)
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var response = await S.SetStatusAsync(ticket.Id(), "approved", Guid.Parse(user));

        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbTheory]
    [InlineData("allocated", "40000000-0000-4000-8000-000000000006")] // only POST .../allocate does this
    [InlineData("closed", "40000000-0000-4000-8000-000000000006")]    // only allocated tickets close
    [InlineData("pending", "40000000-0000-4000-8000-000000000005")]   // already pending
    public async Task Disallowed_transitions_from_pending_are_409(string status, string user)
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var response = await S.SetStatusAsync(ticket.Id(), status, Guid.Parse(user));

        await response.AssertStatusAsync(HttpStatusCode.Conflict);
    }

    [DbFact]
    public async Task Status_and_content_together_is_400()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/tickets/{ticket.Id()}", new
        {
            status = "approved",
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 2 } },
        }, Ids.ItDirector);

        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    [DbFact]
    public async Task Put_on_unknown_ticket_is_404()
    {
        var response = await S.SetStatusAsync(Guid.NewGuid(), "approved", Ids.ItDirector);
        await response.AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbFact]
    public async Task Requester_deletes_pending_ticket()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var other = await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/tickets/{ticket.Id()}", user: Ids.InstructorOkafor);
        await other.AssertStatusAsync(HttpStatusCode.Forbidden);

        var deleted = await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/tickets/{ticket.Id()}", user: Ids.InstructorChen);
        await deleted.AssertStatusAsync(HttpStatusCode.NoContent);

        var fetched = await _client.GetAsync($"/api/v1/assets/tickets/{ticket.Id()}");
        await fetched.AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbFact]
    public async Task Approved_ticket_cannot_be_deleted()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });
        await S.ApproveAsync(ticket.Id());

        var response = await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/tickets/{ticket.Id()}", user: Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.Conflict);
    }

    [DbFact]
    public async Task Asset_requested_by_a_ticket_cannot_be_deleted()
    {
        var asset = await S.PhysicalAsync();
        await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });

        var response = await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/{asset}", user: Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.Conflict);
    }
}
