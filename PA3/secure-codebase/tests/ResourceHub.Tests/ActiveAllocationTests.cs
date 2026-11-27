// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
// AI Contribution: Active allocation view tests, updated for tokens, check-in codes and row-filtered listings (~100%)
// Human Modifications: None yet
// Verification: dotnet test against PostgreSQL 18
// Confidence: High

using System.Net;
using System.Text.Json;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

/// <summary>
/// Builds the data once for the class. Embedded Systems Lab gets: ticket A (2 devices, 1 key,
/// 100 credits; one device returned) and ticket B (1 device, 50 credits; closed).
/// CS501 gets ticket C (1 device), which must not show for the lab.
/// </summary>
public sealed class ActiveAllocationFixture : HubApiFactory
{
    public Guid Device { get; private set; }
    public string ReturnedToken { get; private set; } = "";
    public string KeptToken { get; private set; } = "";
    public string ClosedTicketToken { get; private set; } = "";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        if (TestDb.BaseConnectionString is null)
        {
            return;
        }

        var client = CreateClient();
        var s = new Scenario(client);
        Device = await s.PhysicalAsync(units: 5, loanDays: 14);
        var license = await s.LicenseAsync("LAB-KEY-1", "LAB-KEY-2");
        var credits = await s.VoucherAsync(500m);

        var a = await AllocateForLabAsync(s,
            new { assetId = Device, quantity = 2 },
            new { assetId = license, quantity = 1 },
            new { assetId = credits, amount = 100m });
        var tokensA = Devices(a);
        (ReturnedToken, KeptToken) = (tokensA[0].Token, tokensA[1].Token);
        var returned = await client.SendAsync(HttpMethod.Post, $"/api/v1/assets/{Device}/return",
            new { token = tokensA[0].Token, checkInCode = tokensA[0].CheckInCode }, Ids.Admin);
        await returned.AssertStatusAsync(HttpStatusCode.OK);

        var b = await AllocateForLabAsync(s,
            new { assetId = Device, quantity = 1 },
            new { assetId = credits, amount = 50m });
        ClosedTicketToken = Devices(b)[0].Token;
        await (await s.SetStatusAsync(b.Id("ticketId"), "closed", Ids.Admin)).AssertStatusAsync(HttpStatusCode.OK);

        await s.AllocatedTicketAsync(new { assetId = Device, quantity = 1 });
    }

    private static async Task<JsonElement> AllocateForLabAsync(Scenario s, params object[] items)
    {
        var ticket = await s.SubmitAsync(Ids.AdvisorRaman, new { researchLabId = Ids.LabEmbedded, items });
        await s.ApproveAsync(ticket.Id());
        var response = await s.AllocateAsync(ticket.Id());
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    private static List<IssuedDevice> Devices(JsonElement allocation) =>
        allocation.GetProperty("allocations").EnumerateArray()
            .Where(a => a.Str("assetType") == "physical")
            .SelectMany(a => a.GetProperty("devices").EnumerateArray())
            .Select(d => new IssuedDevice(d.Str("token"), d.Str("checkInCode")))
            .ToList();
}

public class ActiveAllocationTests(ActiveAllocationFixture fx) : IClassFixture<ActiveAllocationFixture>
{
    private readonly HttpClient _client = fx.CreateClient();

    [DbFact]
    public async Task Lab_advisor_sees_active_allocations_for_their_lab()
    {
        var body = await GetAsync($"/api/v1/assets/allocations?researchLabId={Ids.LabEmbedded}", Ids.AdvisorRaman);

        Assert.Equal(Ids.LabEmbedded, body.Id("researchLabId"));
        var allocations = body.GetProperty("allocations").EnumerateArray().ToList();

        // Devices: not yet returned, whatever the ticket status (the closed ticket's device is still out).
        var tokens = allocations.Where(x => x.Str("assetType") == "physical")
            .SelectMany(x => x.GetProperty("devices").EnumerateArray()).Select(d => d.Str("token")).ToList();
        Assert.Equal(new[] { fx.KeptToken, fx.ClosedTicketToken }.Order(), tokens.Order());
        Assert.DoesNotContain(fx.ReturnedToken, tokens);

        // License and voucher: only for tickets that are not closed.
        var key = Assert.Single(allocations, x => x.Str("assetType") == "license");
        Assert.Single(key.GetProperty("licenseKeys").EnumerateArray());
        var credit = Assert.Single(allocations, x => x.Str("assetType") == "voucher");
        Assert.Equal(100m, credit.GetProperty("amount").GetDecimal());

        var summary = body.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("devicesCheckedOut").GetInt32());
        Assert.Equal(0, summary.GetProperty("devicesOverdue").GetInt32());
        Assert.Equal(1, summary.GetProperty("licenseKeysAssigned").GetInt32());
        Assert.Equal(100m, summary.GetProperty("voucherCreditsAllocated").GetDecimal());
    }

    [DbFact]
    public async Task Course_view_shows_only_that_courses_allocations()
    {
        var body = await GetAsync($"/api/v1/assets/allocations?courseId={Ids.CourseCs501}", Ids.InstructorChen);

        var allocation = Assert.Single(body.GetProperty("allocations").EnumerateArray());
        Assert.Equal(fx.Device, allocation.Id("assetId"));
        Assert.Single(allocation.GetProperty("devices").EnumerateArray());

        var other = await GetAsync($"/api/v1/assets/allocations?courseId={Ids.CourseEe580}", Ids.Admin);
        Assert.Empty(other.GetProperty("allocations").EnumerateArray());
    }

    [DbFact]
    public async Task It_director_and_administrator_can_read_any_group()
    {
        await GetAsync($"/api/v1/assets/allocations?researchLabId={Ids.LabEmbedded}", Ids.ItDirector);
        await GetAsync($"/api/v1/assets/allocations?researchLabId={Ids.LabEmbedded}", Ids.Admin);
    }

    [DbFact]
    public async Task Other_groups_see_no_rows_and_anonymous_callers_get_401()
    {
        var url = $"/api/v1/assets/allocations?researchLabId={Ids.LabEmbedded}";
        await (await _client.SendAsync(HttpMethod.Get, url)).AssertStatusAsync(HttpStatusCode.Unauthorized);

        // Faculty from another lab or course may list allocations, but only rows of their own groups come back.
        foreach (var outsider in new[] { Ids.AdvisorFerreira, Ids.InstructorOkafor })
        {
            var body = await GetAsync(url, outsider);
            Assert.Empty(body.GetProperty("allocations").EnumerateArray());
            Assert.Equal(0, body.GetProperty("summary").GetProperty("devicesCheckedOut").GetInt32());
            Assert.DoesNotContain("LAB-KEY", body.GetRawText());
        }

        // A caller with no role has no right to list allocations at all.
        await (await _client.SendAsync(HttpMethod.Get, url, user: Ids.NoRoleUser)).AssertStatusAsync(HttpStatusCode.Forbidden);
    }

    [DbTheory]
    [InlineData("")]
    [InlineData("?courseId=10000000-0000-4000-8000-000000000001&researchLabId=20000000-0000-4000-8000-000000000002")]
    [InlineData("?courseId=19999999-0000-4000-8000-000000000000")]
    public async Task Exactly_one_known_course_or_lab_is_required(string query)
    {
        var response = await _client.SendAsync(HttpMethod.Get, $"/api/v1/assets/allocations{query}", user: Ids.Admin);
        await response.AssertStatusAsync(HttpStatusCode.BadRequest);
    }

    private async Task<JsonElement> GetAsync(string url, Guid user)
    {
        var response = await _client.SendAsync(HttpMethod.Get, url, user: user);
        await response.AssertStatusAsync(HttpStatusCode.OK);
        return await response.JsonAsync();
    }
}
