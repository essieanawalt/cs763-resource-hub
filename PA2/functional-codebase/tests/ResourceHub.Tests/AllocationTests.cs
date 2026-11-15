// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md, test first"
// AI Contribution: Tests for POST /api/v1/assets/tickets/{id}/allocate: issuing, all-or-nothing, concurrency, close (~100%)
// Human Modifications: None yet
// Verification: dotnet test (failed before the allocate route existed, passes after)
// Confidence: High

using System.Net;
using System.Text.Json;
using ResourceHub.Tests.Infrastructure;

namespace ResourceHub.Tests;

public class AllocationTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbFact]
    public async Task Allocating_license_issues_distinct_keys_from_the_pool()
    {
        var keys = new[] { "KEY-AAA-1", "KEY-BBB-2", "KEY-CCC-3" };
        var asset = await S.LicenseAsync(keys);

        var result = await S.AllocatedTicketAsync(new { assetId = asset, quantity = 2 });

        Assert.Equal("allocated", result.Str("status"));
        var allocation = Assert.Single(result.GetProperty("allocations").EnumerateArray());
        Assert.Equal("license", allocation.Str("assetType"));
        var issued = allocation.GetProperty("licenseKeys").EnumerateArray().Select(k => k.GetString()!).ToList();
        Assert.Equal(2, issued.Distinct().Count());
        Assert.All(issued, k => Assert.Contains(k, keys));

        var after = await S.GetAssetAsync(asset);
        Assert.Equal(1, after.GetProperty("remainingStock").GetDecimal());
        Assert.Equal(2, after.GetProperty("availability").GetProperty("issued").GetInt32());
    }

    [DbFact]
    public async Task Allocating_voucher_deducts_the_amount_from_the_balance()
    {
        var asset = await S.VoucherAsync(1000m);

        var result = await S.AllocatedTicketAsync(new { assetId = asset, amount = 400.50m });

        var allocation = Assert.Single(result.GetProperty("allocations").EnumerateArray());
        Assert.Equal(400.50m, allocation.GetProperty("amount").GetDecimal());
        var after = await S.GetAssetAsync(asset);
        Assert.Equal(599.50m, after.GetProperty("availability").GetProperty("balance").GetDecimal());
        Assert.Equal(400.50m, after.GetProperty("availability").GetProperty("allocated").GetDecimal());
    }

    [DbFact]
    public async Task Allocating_physical_issues_one_token_per_device_with_due_dates()
    {
        var shortLoan = await S.PhysicalAsync(units: 3, loanDays: 14);
        var defaultLoan = await S.PhysicalAsync(units: 2, loanDays: 30);
        var now = factory.Clock.GetUtcNow();

        var result = await S.AllocatedTicketAsync(
            new { assetId = shortLoan, quantity = 2, loanDays = 7 },
            new { assetId = defaultLoan, quantity = 1 });

        var allocations = result.GetProperty("allocations").EnumerateArray().ToList();
        var shortDevices = allocations.Single(a => a.Id("assetId") == shortLoan).GetProperty("devices").EnumerateArray().ToList();
        Assert.Equal(2, shortDevices.Count);
        Assert.Equal(2, shortDevices.Select(d => d.Str("token")).Distinct().Count());
        Assert.All(shortDevices, d => Assert.Equal(now.AddDays(7), d.GetProperty("dueAt").GetDateTimeOffset()));
        var defaultDevice = Assert.Single(allocations.Single(a => a.Id("assetId") == defaultLoan).GetProperty("devices").EnumerateArray());
        Assert.Equal(now.AddDays(30), defaultDevice.GetProperty("dueAt").GetDateTimeOffset());

        var after = await S.GetAssetAsync(shortLoan);
        Assert.Equal(1, after.GetProperty("remainingStock").GetDecimal());
        Assert.Equal(2, after.GetProperty("availability").GetProperty("checkedout").GetInt32());
        Assert.Equal(0, after.GetProperty("availability").GetProperty("reserved").GetInt32());
    }

    [DbFact]
    public async Task Allocation_is_all_or_nothing_and_can_be_retried_later()
    {
        var device = await S.PhysicalAsync(units: 2);
        var credits = await S.VoucherAsync(100m);
        var ticket = await S.SubmitForCourseAsync(
            new { assetId = device, quantity = 1 },
            new { assetId = credits, amount = 150m });
        await S.ApproveAsync(ticket.Id());

        var failed = await S.AllocateAsync(ticket.Id());

        await failed.AssertStatusAsync(HttpStatusCode.Conflict);
        var problem = await failed.JsonAsync();
        var shortage = Assert.Single(problem.GetProperty("shortages").EnumerateArray());
        Assert.Equal(credits, shortage.Id("assetId"));
        Assert.Equal(150m, shortage.GetProperty("requested").GetDecimal());
        Assert.Equal(100m, shortage.GetProperty("available").GetDecimal());

        // Nothing moved, and the ticket is still approved.
        Assert.Equal(2, (await S.GetAssetAsync(device)).GetProperty("remainingStock").GetDecimal());
        Assert.Equal(100m, (await S.GetAssetAsync(credits)).GetProperty("remainingStock").GetDecimal());
        var detail = await S.GetTicketAsync(ticket.Id());
        Assert.Equal("approved", detail.Str("status"));
        Assert.Contains(detail.GetProperty("events").EnumerateArray(), e => e.Str("type") == "allocation_failed");

        // Top up the pool, then the same ticket allocates.
        var topUp = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{credits}", new
        {
            name = "Test credits", type = "voucher", category = "Test credits", departmentProgram = "Computer Science", balance = 200m,
        }, Ids.Admin);
        await topUp.AssertStatusAsync(HttpStatusCode.OK);

        var retried = await S.AllocateAsync(ticket.Id());
        await retried.AssertStatusAsync(HttpStatusCode.OK);
        Assert.Equal(1, (await S.GetAssetAsync(device)).GetProperty("remainingStock").GetDecimal());
        Assert.Equal(50m, (await S.GetAssetAsync(credits)).GetProperty("remainingStock").GetDecimal());
    }

    [DbFact]
    public async Task Only_approved_tickets_allocate()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetLaptop, quantity = 1 });

        var response = await S.AllocateAsync(ticket.Id());

        await response.AssertStatusAsync(HttpStatusCode.Conflict);
    }

    [DbFact]
    public async Task Allocating_twice_is_409()
    {
        var asset = await S.PhysicalAsync(units: 5);
        var ticket = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });
        await S.ApproveAsync(ticket.Id());
        await (await S.AllocateAsync(ticket.Id())).AssertStatusAsync(HttpStatusCode.OK);

        var again = await S.AllocateAsync(ticket.Id());

        await again.AssertStatusAsync(HttpStatusCode.Conflict);
        Assert.Equal(4, (await S.GetAssetAsync(asset)).GetProperty("remainingStock").GetDecimal());
    }

    [DbFact]
    public async Task Only_administrators_allocate_and_unknown_ticket_is_404()
    {
        var asset = await S.PhysicalAsync();
        var ticket = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });
        await S.ApproveAsync(ticket.Id());

        var forbidden = await S.AllocateAsync(ticket.Id(), Ids.ItDirector);
        await forbidden.AssertStatusAsync(HttpStatusCode.Forbidden);

        var missing = await S.AllocateAsync(Guid.NewGuid());
        await missing.AssertStatusAsync(HttpStatusCode.NotFound);
    }

    [DbFact]
    public async Task Concurrent_allocations_never_overdraw_stock()
    {
        var asset = await S.PhysicalAsync(units: 1);
        var tickets = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var t = await S.SubmitForCourseAsync(new { assetId = asset, quantity = 1 });
            await S.ApproveAsync(t.Id());
            tickets.Add(t.Id());
        }

        var responses = await Task.WhenAll(tickets.Select(id => factory.CreateClient().SendAsync(
            HttpMethod.Post, $"/api/v1/assets/tickets/{id}/allocate", user: Ids.Admin)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, (await S.GetAssetAsync(asset)).GetProperty("remainingStock").GetDecimal());
    }

    [DbFact]
    public async Task Administrator_closes_allocated_ticket()
    {
        var asset = await S.VoucherAsync(50m);
        var result = await S.AllocatedTicketAsync(new { assetId = asset, amount = 10m });
        var ticketId = result.Id("ticketId");

        var director = await S.SetStatusAsync(ticketId, "closed", Ids.ItDirector);
        await director.AssertStatusAsync(HttpStatusCode.Forbidden);

        var closed = await S.SetStatusAsync(ticketId, "closed", Ids.Admin);
        await closed.AssertStatusAsync(HttpStatusCode.OK);
        Assert.Equal("closed", (await closed.JsonAsync()).Str("status"));

        var detail = await S.GetTicketAsync(ticketId);
        Assert.Equal(
            ["submitted", "approved", "allocation_succeeded", "closed"],
            detail.GetProperty("events").EnumerateArray().Select(e => e.Str("type")).ToArray());
    }

    [DbFact]
    public async Task Total_stock_cannot_drop_below_devices_handed_out()
    {
        var asset = await S.PhysicalAsync(units: 3);
        await S.AllocatedTicketAsync(new { assetId = asset, quantity = 2 });

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{asset}", new
        {
            name = "Test kit", type = "physical", category = "Test device", departmentProgram = "Computer Science",
            units = 1, defaultLoanDays = 14,
        }, Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.Conflict);
    }

    [DbFact]
    public async Task Issued_license_keys_cannot_be_removed_by_update()
    {
        var asset = await S.LicenseAsync("ONLY-KEY-1");
        await S.AllocatedTicketAsync(new { assetId = asset, quantity = 1 });

        var response = await _client.SendAsync(HttpMethod.Put, $"/api/v1/assets/{asset}", new
        {
            name = "Test license", type = "license", category = "Test license", departmentProgram = "Computer Science",
            licenseKeys = new[] { "OTHER-KEY-2" },
        }, Ids.Admin);

        await response.AssertStatusAsync(HttpStatusCode.Conflict);
    }
}

/// <summary>Uses the seeded JetBrains license (3 keys) in a database of its own.</summary>
public class SeededLicenseOverAllocationTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbFact]
    public async Task Requesting_more_keys_than_exist_fails_and_leaves_all_keys_in_the_pool()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetJetBrains, quantity = 4 });
        await S.ApproveAsync(ticket.Id());

        var response = await S.AllocateAsync(ticket.Id());

        await response.AssertStatusAsync(HttpStatusCode.Conflict);
        var problem = await response.JsonAsync();
        var shortage = Assert.Single(problem.GetProperty("shortages").EnumerateArray());
        Assert.Equal(4, shortage.GetProperty("requested").GetDecimal());
        Assert.Equal(3, shortage.GetProperty("available").GetDecimal());

        var asset = await S.GetAssetAsync(Ids.AssetJetBrains);
        Assert.Equal(3, asset.GetProperty("remainingStock").GetDecimal());
        var detail = await S.GetTicketAsync(ticket.Id());
        Assert.Equal("approved", detail.Str("status"));
        Assert.Equal(1, detail.GetProperty("metrics").GetProperty("failedAllocationAttempts").GetInt32());
    }
}

/// <summary>Uses the seeded JetBrains license (3 keys) in a database of its own.</summary>
public class SeededLicenseFullAllocationTests(HubApiFactory factory) : IClassFixture<HubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private Scenario S => new(_client);

    [DbFact]
    public async Task Requesting_exactly_the_seeded_keys_issues_all_three()
    {
        var ticket = await S.SubmitForCourseAsync(new { assetId = Ids.AssetJetBrains, quantity = 3 });
        await S.ApproveAsync(ticket.Id());

        var response = await S.AllocateAsync(ticket.Id());

        await response.AssertStatusAsync(HttpStatusCode.OK);
        var allocation = Assert.Single((await response.JsonAsync()).GetProperty("allocations").EnumerateArray());
        var keys = allocation.GetProperty("licenseKeys").EnumerateArray().Select(k => k.GetString()!).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(["JBIDE-3PLR-8WVN", "JBIDE-7K2M-Q9XA", "JBIDE-M4TD-2HCE"], keys);
        Assert.Equal(0, (await S.GetAssetAsync(Ids.AssetJetBrains)).GetProperty("remainingStock").GetDecimal());
    }
}
