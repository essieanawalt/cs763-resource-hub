// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the /api/v1/assets/tickets integration tests (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using System.Net;
using ResourceHub.Api.Data;

namespace ResourceHub.Tests;

public class TicketEndpointTests : IClassFixture<HubApiFactory>
{
    private readonly HubApiFactory api;

    public TicketEndpointTests(HubApiFactory api)
    {
        this.api = api;
    }

    [DatabaseFact]
    public async Task Post_submits_multi_item_ticket_and_get_returns_it_with_events()
    {
        var response = await api.As(MockData.Ids.Chen).PostJson("/api/v1/assets/tickets", new
        {
            courseId = MockData.Ids.CourseCs501,
            projectId = MockData.Ids.ProjectDashboard,
            items = new object[]
            {
                new { assetId = MockData.Ids.Laptop, quantity = 2, loanDays = 10 },
                new { assetId = MockData.Ids.CloudCredits, amount = 250.50m },
            },
        });
        var created = await response.ReadAsync(HttpStatusCode.Created);
        Assert.Equal("pending", created["status"]!.GetValue<string>());

        var detail = await (await api.As(MockData.Ids.Chen).GetAsync(response.Headers.Location))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Equal(2, detail["items"]!.AsArray().Count);
        Assert.Equal("submitted", Assert.Single(detail["events"]!.AsArray())!["type"]!.GetValue<string>());
        Assert.NotNull(detail["timings"]);
    }

    [DatabaseFact]
    public async Task Get_list_filters_by_status_course_and_asset_category()
    {
        var category = Steps.NewCategory();
        var assetId = await Steps.CreatePhysicalAsync(api, category, units: 3);
        var pending = await Steps.SubmitAsync(api, new { assetId, quantity = 1 });
        api.Clock.Advance(TimeSpan.FromSeconds(1));
        var approved = await Steps.SubmitAsync(api, new { assetId, quantity = 1 });
        await Steps.ApproveAsync(api, approved);

        var page = await (await api.As(MockData.Ids.Blake).GetAsync(
                $"/api/v1/assets/tickets?status=approved&courseId={MockData.Ids.CourseCs501}&assetCategory={category}"))
            .ReadAsync(HttpStatusCode.OK);

        var item = Assert.Single(page["items"]!.AsArray())!;
        Assert.Equal(approved.ToString(), item["id"]!.GetValue<string>());

        var all = await (await api.As(MockData.Ids.Blake).GetAsync($"/api/v1/assets/tickets?assetCategory={category}"))
            .ReadAsync(HttpStatusCode.OK);
        var ids = all["items"]!.AsArray().Select(t => t!["id"]!.GetValue<string>()).ToList();
        // Newest first by default.
        Assert.Equal([approved.ToString(), pending.ToString()], ids);
        Assert.Equal(2, all["totalCount"]!.GetValue<int>());
    }

    [DatabaseFact]
    public async Task Put_by_requester_replaces_contents_of_pending_ticket()
    {
        var ticketId = await Steps.SubmitAsync(api, new { assetId = MockData.Ids.Laptop, quantity = 1 });

        var updated = await (await api.As(MockData.Ids.Chen).PutJson($"/api/v1/assets/tickets/{ticketId}", new
        {
            notes = "Need one more",
            items = new object[] { new { assetId = MockData.Ids.Laptop, quantity = 2 }, new { assetId = MockData.Ids.JetBrains, quantity = 1 } },
        })).ReadAsync(HttpStatusCode.OK);

        Assert.Equal("Need one more", updated["notes"]!.GetValue<string>());
        Assert.Equal(2, updated["items"]!.AsArray().Count);
        Assert.Contains(updated["events"]!.AsArray(), e => e!["type"]!.GetValue<string>() == "updated");
    }

    [DatabaseFact]
    public async Task Put_status_by_it_director_approves_and_records_sign_off_timing()
    {
        var ticketId = await Steps.SubmitAsync(api, new { assetId = MockData.Ids.Laptop, quantity = 1 });

        // Only the IT director signs off.
        var byInstructor = await api.As(MockData.Ids.Chen)
            .PutJson($"/api/v1/assets/tickets/{ticketId}", new { status = "approved" });
        Assert.Equal(HttpStatusCode.Forbidden, byInstructor.StatusCode);

        api.Clock.Advance(TimeSpan.FromMinutes(5));
        var approved = await (await api.As(MockData.Ids.Blake)
            .PutJson($"/api/v1/assets/tickets/{ticketId}", new { status = "approved", comment = "Budget OK" }))
            .ReadAsync(HttpStatusCode.OK);

        Assert.Equal("approved", approved["status"]!.GetValue<string>());
        Assert.Equal(MockData.Ids.Blake.ToString(), approved["signedOffById"]!.GetValue<string>());
        Assert.Equal(300d, approved["timings"]!["submissionToSignOffSeconds"]!.GetValue<double>());

        // Contents are frozen once approved, and allocated is reachable only through /allocate.
        var edit = await api.As(MockData.Ids.Chen).PutJson($"/api/v1/assets/tickets/{ticketId}",
            new { items = new object[] { new { assetId = MockData.Ids.Laptop, quantity = 3 } } });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        var skip = await api.As(MockData.Ids.Blake).PutJson($"/api/v1/assets/tickets/{ticketId}", new { status = "allocated" });
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);
    }

    [DatabaseFact]
    public async Task Put_status_rejects_pending_ticket()
    {
        var ticketId = await Steps.SubmitAsync(api, new { assetId = MockData.Ids.Laptop, quantity = 1 });

        var rejected = await (await api.As(MockData.Ids.Blake)
            .PutJson($"/api/v1/assets/tickets/{ticketId}", new { status = "rejected", comment = "Out of budget" }))
            .ReadAsync(HttpStatusCode.OK);

        Assert.Equal("rejected", rejected["status"]!.GetValue<string>());
        Assert.Contains(rejected["events"]!.AsArray(),
            e => e!["type"]!.GetValue<string>() == "rejected" && e["detail"]!.GetValue<string>() == "Out of budget");
    }

    [DatabaseFact]
    public async Task Delete_removes_pending_ticket_but_not_approved_one()
    {
        var pending = await Steps.SubmitAsync(api, new { assetId = MockData.Ids.Laptop, quantity = 1 });
        var approved = await Steps.SubmitAsync(api, new { assetId = MockData.Ids.Laptop, quantity = 1 });
        await Steps.ApproveAsync(api, approved);
        var chen = api.As(MockData.Ids.Chen);

        Assert.Equal(HttpStatusCode.NoContent, (await chen.DeleteAsync($"/api/v1/assets/tickets/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await chen.GetAsync($"/api/v1/assets/tickets/{pending}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chen.DeleteAsync($"/api/v1/assets/tickets/{approved}")).StatusCode);
    }

    [DatabaseFact]
    public async Task Requesters_are_limited_to_their_own_course_or_lab()
    {
        // Dr. Chen teaches CS501, not EE580.
        var otherCourse = await api.As(MockData.Ids.Chen).PostJson("/api/v1/assets/tickets", new
        {
            courseId = MockData.Ids.CourseEe580,
            items = new object[] { new { assetId = MockData.Ids.RaspberryPi, quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, otherCourse.StatusCode);

        // Dr. Raman (Embedded Systems Lab) cannot read or list CS501's ticket.
        var ticketId = await Steps.SubmitAsync(api, new { assetId = MockData.Ids.Laptop, quantity = 1 });
        var raman = api.As(MockData.Ids.Raman);
        Assert.Equal(HttpStatusCode.NotFound, (await raman.GetAsync($"/api/v1/assets/tickets/{ticketId}")).StatusCode);
        var list = await (await raman.GetAsync($"/api/v1/assets/tickets?courseId={MockData.Ids.CourseCs501}"))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Empty(list["items"]!.AsArray());

        // The IT director cannot submit tickets.
        var byDirector = await api.As(MockData.Ids.Blake).PostJson("/api/v1/assets/tickets", new
        {
            courseId = MockData.Ids.CourseCs501,
            items = new object[] { new { assetId = MockData.Ids.Laptop, quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, byDirector.StatusCode);
    }

    [DatabaseFact]
    public async Task Post_rejects_amount_on_physical_item()
    {
        var response = await api.As(MockData.Ids.Chen).PostJson("/api/v1/assets/tickets", new
        {
            courseId = MockData.Ids.CourseCs501,
            items = new object[] { new { assetId = MockData.Ids.Laptop, amount = 5 } },
        });
        await response.ReadAsync(HttpStatusCode.BadRequest);
    }
}
