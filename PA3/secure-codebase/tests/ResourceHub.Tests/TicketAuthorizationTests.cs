// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Row-level authorization and role refusal tests for tickets (CWE-862), including the 404/403 rule (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18
// Confidence: High

using System.Net;
using System.Text.Json;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class TicketAuthorizationTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    private Task<JsonElement> ChenTicketAsync() => S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

    private Task<JsonElement> OkaforTicketAsync() => S.SubmitAsync(Ids.InstructorOkafor, new
    {
        courseId = Ids.CourseEe580,
        notes = "Okafor private note",
        items = new[] { new { assetId = Ids.AssetRaspberryPi, quantity = 1 } },
    });

    // ---- reads ----

    [DbFact]
    public async Task Faculty_reads_own_ticket_admin_reads_any()
    {
        var ticket = await ChenTicketAsync();
        var url = $"/api/v1/assets/tickets/{ticket.Id()}";

        await (await _client.GetAsync(url, Ids.InstructorChen)).AssertStatusAsync(HttpStatusCode.OK);
        await (await _client.GetAsync(url, Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);
        await (await _client.GetAsync(url, Ids.ItDirector)).AssertStatusAsync(HttpStatusCode.OK);
    }

    [DbFact]
    public async Task Another_faculty_member_gets_the_same_404_as_for_a_missing_ticket()
    {
        var ticket = await ChenTicketAsync();

        var other = await _client.GetAsync($"/api/v1/assets/tickets/{ticket.Id()}", Ids.InstructorOkafor);
        var missing = await _client.GetAsync($"/api/v1/assets/tickets/{Guid.NewGuid()}", Ids.InstructorOkafor);

        await other.AssertStatusAsync(HttpStatusCode.NotFound);
        await missing.AssertStatusAsync(HttpStatusCode.NotFound);
        var a = await other.JsonAsync();
        var b = await missing.JsonAsync();
        Assert.Equal(b.Str("title"), a.Str("title"));
        Assert.DoesNotContain(ticket.Id().ToString(), a.GetRawText());
        Assert.DoesNotContain(Ids.InstructorChen.ToString(), a.GetRawText());
    }

    [DbFact]
    public async Task Caller_without_a_role_gets_403_on_tickets()
    {
        var ticket = await ChenTicketAsync();

        await (await _client.GetAsync($"/api/v1/assets/tickets/{ticket.Id()}", Ids.NoRoleUser)).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await _client.GetAsync("/api/v1/assets/tickets", Ids.NoRoleUser)).AssertStatusAsync(HttpStatusCode.Forbidden);
        var submit = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 1 } },
        }, Ids.NoRoleUser);
        await submit.AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/tickets/{ticket.Id()}", user: Ids.NoRoleUser))
            .AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbFact]
    public async Task Seeded_user_whose_token_has_no_role_gets_no_rights()
    {
        // Dr. Chen's identity but an empty roles claim: roles come from the token only.
        var token = TestTokens.For(Ids.InstructorChen, []);
        var ticket = await ChenTicketAsync();

        await (await _client.SendAsync(HttpMethod.Get, $"/api/v1/assets/tickets/{ticket.Id()}", token: token)).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await _client.SendAsync(HttpMethod.Get, "/api/v1/assets", token: token)).AssertStatusAsync(HttpStatusCode.OK);
    }

    // ---- listing ----

    [DbFact]
    public async Task Faculty_listing_returns_only_their_own_tickets()
    {
        var chen = await ChenTicketAsync();
        var okafor = await OkaforTicketAsync();

        var chenList = await ListAsync("/api/v1/assets/tickets?pageSize=100", Ids.InstructorChen);
        var ids = chenList.GetProperty("items").EnumerateArray().Select(t => t.Id()).ToList();
        Assert.Contains(chen.Id(), ids);
        Assert.DoesNotContain(okafor.Id(), ids);
        Assert.All(chenList.GetProperty("items").EnumerateArray(), t => Assert.Equal(Ids.InstructorChen, t.Id("requesterId")));
        Assert.Equal(ids.Count, chenList.GetProperty("total").GetInt32());
        Assert.DoesNotContain("Okafor private note", chenList.GetRawText());

        // Filtering on someone else's course does not widen the view.
        var filtered = await ListAsync($"/api/v1/assets/tickets?courseId={Ids.CourseEe580}", Ids.InstructorChen);
        Assert.Equal(0, filtered.GetProperty("total").GetInt32());

        var admin = await ListAsync("/api/v1/assets/tickets?pageSize=100", Ids.Admin);
        var all = admin.GetProperty("items").EnumerateArray().Select(t => t.Id()).ToList();
        Assert.Contains(chen.Id(), all);
        Assert.Contains(okafor.Id(), all);
    }

    // ---- writes ----

    [DbFact]
    public async Task Faculty_cannot_update_or_delete_another_users_ticket_and_it_is_unchanged()
    {
        var okafor = await OkaforTicketAsync();
        var url = $"/api/v1/assets/tickets/{okafor.Id()}";

        var update = await _client.SendAsync(HttpMethod.Put, url, new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 5 } },
        }, Ids.InstructorChen);
        await update.AssertStatusAsync(HttpStatusCode.NotFound);
        await (await _client.SendAsync(HttpMethod.Delete, url, user: Ids.InstructorChen)).AssertStatusAsync(HttpStatusCode.NotFound);

        var after = await S.GetTicketAsync(okafor.Id());
        Assert.Equal("pending", after.Str("status"));
        Assert.Equal(Ids.AssetRaspberryPi, Assert.Single(after.GetProperty("items").EnumerateArray()).Id("assetId"));
    }

    [DbFact]
    public async Task Faculty_cannot_update_or_delete_own_ticket_once_it_is_not_pending()
    {
        var ticket = await ChenTicketAsync();
        await S.ApproveAsync(ticket.Id());
        var url = $"/api/v1/assets/tickets/{ticket.Id()}";

        var update = await _client.SendAsync(HttpMethod.Put, url, new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 2 } },
        }, Ids.InstructorChen);
        await update.AssertStatusAsync(HttpStatusCode.Conflict);
        await (await _client.SendAsync(HttpMethod.Delete, url, user: Ids.InstructorChen)).AssertStatusAsync(HttpStatusCode.Conflict);
    }

    [DbTheory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("closed")]
    public async Task Faculty_cannot_change_ticket_status_even_on_their_own_ticket(string status)
    {
        var ticket = await ChenTicketAsync();

        var response = await S.SetStatusAsync(ticket.Id(), status, Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
        Assert.Equal("pending", (await S.GetTicketAsync(ticket.Id())).Str("status"));
    }

    [DbFact]
    public async Task Faculty_cannot_allocate_and_admin_can()
    {
        var ticket = await ChenTicketAsync();
        await S.ApproveAsync(ticket.Id());

        await (await S.AllocateAsync(ticket.Id(), Ids.InstructorChen)).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await S.AllocateAsync(ticket.Id(), Ids.NoRoleUser)).AssertStatusAsync(HttpStatusCode.Forbidden);
        await (await S.AllocateAsync(ticket.Id(), Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);
    }

    [DbFact]
    public async Task Admin_deletes_any_pending_ticket()
    {
        var okafor = await OkaforTicketAsync();
        var url = $"/api/v1/assets/tickets/{okafor.Id()}";

        await (await _client.SendAsync(HttpMethod.Delete, url, user: Ids.Admin)).AssertStatusAsync(HttpStatusCode.NoContent);
        await (await _client.GetAsync(url, Ids.Admin)).AssertStatusAsync(HttpStatusCode.NotFound);
        await (await _client.GetAsync(url, Ids.InstructorOkafor)).AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbTheory]
    [InlineData("40000000-0000-4000-8000-000000000005")] // IT director (admin)
    [InlineData("40000000-0000-4000-8000-000000000006")] // administrator (admin)
    public async Task Admin_approves_and_rejects(string user)
    {
        var a = await ChenTicketAsync();
        var b = await ChenTicketAsync();

        await (await S.SetStatusAsync(a.Id(), "approved", Guid.Parse(user))).AssertStatusAsync(HttpStatusCode.OK);
        await (await S.SetStatusAsync(b.Id(), "rejected", Guid.Parse(user), "No budget")).AssertStatusAsync(HttpStatusCode.OK);
    }

    [DbFact]
    public async Task Faculty_cannot_submit_for_a_course_they_are_not_associated_with()
    {
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", new
        {
            courseId = Ids.CourseEe580,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 1 } },
        }, Ids.InstructorChen);

        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbFact]
    public async Task Faculty_role_with_unknown_sub_has_no_association_and_cannot_submit()
    {
        var token = TestTokens.For(Guid.NewGuid(), [TestTokens.FacultyRole]);
        var response = await _client.SendAsync(HttpMethod.Post, "/api/v1/assets/tickets", new
        {
            courseId = Ids.CourseCs501,
            items = new[] { new { assetId = Ids.AssetLaptop, quantity = 1 } },
        }, token: token);

        await response.AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    // ---- assets: catalog open, writes admin only ----

    [DbFact]
    public async Task Any_authenticated_caller_reads_and_searches_the_catalog()
    {
        foreach (var user in new[] { Ids.NoRoleUser, Ids.InstructorChen, Ids.Admin })
        {
            await (await _client.GetAsync("/api/v1/assets?search=laptop", user)).AssertStatusAsync(HttpStatusCode.OK);
            await (await _client.GetAsync($"/api/v1/assets/{Ids.AssetLaptop}", user)).AssertStatusAsync(HttpStatusCode.OK);
        }
    }

    [DbFact]
    public async Task Only_admin_writes_assets_and_processes_returns()
    {
        var body = new { name = "X", type = "voucher", category = "Cloud", departmentProgram = "CS", balance = 10 };
        foreach (var user in new[] { Ids.NoRoleUser, Ids.InstructorChen })
        {
            await (await _client.SendAsync(HttpMethod.Post, "/api/v1/assets", body, user)).AssertStatusAsync(HttpStatusCode.Forbidden);
            await (await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{Ids.AssetCloudCredits}", body, user)).AssertStatusAsync(HttpStatusCode.Forbidden);
            await (await _client.SendAsync(HttpMethod.Delete, $"/api/v1/assets/{Ids.AssetCloudCredits}", user: user)).AssertStatusAsync(HttpStatusCode.Forbidden);
            await (await _client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{Ids.AssetLaptop}/return", new { token = "HW-X", checkInCode = "CI-X" }, user))
                .AssertStatusAsync(HttpStatusCode.Forbidden);
        }
    }

    private async Task<JsonElement> ListAsync(string url, Guid user)
    {
        var response = await _client.GetAsync(url, user);
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }
}
