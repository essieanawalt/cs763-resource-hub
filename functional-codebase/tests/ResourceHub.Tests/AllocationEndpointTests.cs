// Tool: Claude Code (Claude Opus 5.5)
// Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
// Prompt Summary: "Build the Resource Allocation & Help Desk Hub from spec-domain2.md (PA2)"
// AI Contribution: Wrote the allocate, return and allocations integration tests (~100%)
// Human Modifications: None yet
// Verification: dotnet build; 26 of 26 xUnit tests passed against PostgreSQL 18; docker compose walkthrough (2026-11-14)
// Confidence: High

using System.Net;
using ResourceHub.Api.Data;

namespace ResourceHub.Tests;

public class AllocationEndpointTests : IClassFixture<HubApiFactory>
{
    private readonly HubApiFactory api;

    public AllocationEndpointTests(HubApiFactory api)
    {
        this.api = api;
    }

    [DatabaseFact]
    public async Task Allocate_issues_tokens_keys_and_credit_and_deducts_stock()
    {
        var category = Steps.NewCategory();
        var device = await Steps.CreatePhysicalAsync(api, category, units: 5, loanDays: 14);
        var license = await Steps.CreateLicenseAsync(api, category, $"K-{Guid.NewGuid():N}", $"K-{Guid.NewGuid():N}");
        var voucher = await Steps.CreateVoucherAsync(api, category, 1000m);
        var ticketId = await Steps.SubmitAsync(api,
            new { assetId = device, quantity = 2, loanDays = 7 },
            new { assetId = license, quantity = 1 },
            new { assetId = voucher, amount = 400.25m });
        await Steps.ApproveAsync(api, ticketId);

        var result = await (await api.As(MockData.Ids.Rivera).PostAsync($"/api/v1/assets/tickets/{ticketId}/allocate", null))
            .ReadAsync(HttpStatusCode.OK);

        Assert.Equal("allocated", result["status"]!.GetValue<string>());
        var devices = result["devices"]!.AsArray();
        Assert.Equal(2, devices.Count);
        Assert.All(devices, d => Assert.StartsWith("HW-", d!["token"]!.GetValue<string>()));
        var due = devices[0]!["dueAt"]!.GetValue<DateTimeOffset>() - devices[0]!["checkedOutAt"]!.GetValue<DateTimeOffset>();
        Assert.Equal(TimeSpan.FromDays(7), due);
        Assert.StartsWith("K-", Assert.Single(result["licenseKeys"]!.AsArray())!["key"]!.GetValue<string>());
        Assert.Equal(400.25m, Assert.Single(result["vouchers"]!.AsArray())!["amount"]!.GetValue<decimal>());

        Assert.Equal(3m, (await Steps.GetAssetAsync(api, device))["remainingStock"]!.GetValue<decimal>());
        Assert.Equal(1m, (await Steps.GetAssetAsync(api, license))["remainingStock"]!.GetValue<decimal>());
        Assert.Equal(599.75m, (await Steps.GetAssetAsync(api, voucher))["remainingStock"]!.GetValue<decimal>());

        var ticket = await (await api.As(MockData.Ids.Chen).GetAsync($"/api/v1/assets/tickets/{ticketId}"))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Equal(1, ticket["timings"]!["allocationAttempts"]!.GetValue<int>());
        Assert.NotNull(ticket["timings"]!["approvalToAllocationSeconds"]);
    }

    [DatabaseFact]
    public async Task Allocating_more_license_keys_than_exist_allocates_nothing()
    {
        var category = Steps.NewCategory();
        var license = await Steps.CreateLicenseAsync(api, category, $"K-{Guid.NewGuid():N}", $"K-{Guid.NewGuid():N}");
        var voucher = await Steps.CreateVoucherAsync(api, category, 500m);
        var ticketId = await Steps.SubmitAsync(api,
            new { assetId = license, quantity = 3 },
            new { assetId = voucher, amount = 100m });
        await Steps.ApproveAsync(api, ticketId);
        var admin = api.As(MockData.Ids.Rivera);

        var failed = await (await admin.PostAsync($"/api/v1/assets/tickets/{ticketId}/allocate", null))
            .ReadAsync(HttpStatusCode.Conflict);
        var shortage = Assert.Single(failed["shortages"]!.AsArray())!;
        Assert.Equal(license.ToString(), shortage["assetId"]!.GetValue<string>());
        Assert.Equal(3m, shortage["requested"]!.GetValue<decimal>());
        Assert.Equal(2m, shortage["available"]!.GetValue<decimal>());

        // All or nothing: the voucher balance and keys are untouched and the ticket stays approved.
        Assert.Equal(500m, (await Steps.GetAssetAsync(api, voucher))["remainingStock"]!.GetValue<decimal>());
        Assert.Equal(2m, (await Steps.GetAssetAsync(api, license))["remainingStock"]!.GetValue<decimal>());
        var ticket = await (await admin.GetAsync($"/api/v1/assets/tickets/{ticketId}")).ReadAsync(HttpStatusCode.OK);
        Assert.Equal("approved", ticket["status"]!.GetValue<string>());
        Assert.Equal(1, ticket["timings"]!["failedAllocationAttempts"]!.GetValue<int>());

        // Once a key is added the same ticket can be allocated.
        await (await admin.PutJson($"/api/v1/assets/{license}", new
        {
            name = "Test license", category, departmentProgram = "Computer Science",
            addLicenseKeys = new[] { $"K-{Guid.NewGuid():N}" },
        })).ReadAsync(HttpStatusCode.OK);
        var retry = await (await admin.PostAsync($"/api/v1/assets/tickets/{ticketId}/allocate", null))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Equal(3, retry["licenseKeys"]!.AsArray().Count);
    }

    [DatabaseFact]
    public async Task Allocate_refuses_ticket_that_is_not_approved()
    {
        var ticketId = await Steps.SubmitAsync(api, new { assetId = MockData.Ids.Laptop, quantity = 1 });

        var response = await api.As(MockData.Ids.Rivera).PostAsync($"/api/v1/assets/tickets/{ticketId}/allocate", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var byDirector = await api.As(MockData.Ids.Blake).PostAsync($"/api/v1/assets/tickets/{ticketId}/allocate", null);
        Assert.Equal(HttpStatusCode.Forbidden, byDirector.StatusCode);
    }

    [DatabaseFact]
    public async Task Concurrent_allocations_cannot_spend_the_same_unit()
    {
        var category = Steps.NewCategory();
        var device = await Steps.CreatePhysicalAsync(api, category, units: 1);
        var first = await Steps.SubmitAsync(api, new { assetId = device, quantity = 1 });
        var second = await Steps.SubmitAsync(api, new { assetId = device, quantity = 1 });
        await Steps.ApproveAsync(api, first);
        await Steps.ApproveAsync(api, second);

        var responses = await Task.WhenAll(
            api.As(MockData.Ids.Rivera).PostAsync($"/api/v1/assets/tickets/{first}/allocate", null),
            api.As(MockData.Ids.Rivera).PostAsync($"/api/v1/assets/tickets/{second}/allocate", null));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(0m, (await Steps.GetAssetAsync(api, device))["remainingStock"]!.GetValue<decimal>());
    }

    [DatabaseFact]
    public async Task Return_adds_one_unit_and_records_the_time()
    {
        var device = await Steps.CreatePhysicalAsync(api, Steps.NewCategory(), units: 2);
        var ticketId = await Steps.SubmitAsync(api, new { assetId = device, quantity = 2 });
        await Steps.ApproveAsync(api, ticketId);
        var admin = api.As(MockData.Ids.Rivera);
        var allocated = await (await admin.PostAsync($"/api/v1/assets/tickets/{ticketId}/allocate", null))
            .ReadAsync(HttpStatusCode.OK);
        var token = allocated["devices"]![0]!["token"]!.GetValue<string>();

        var returned = await (await admin.PostJson($"/api/v1/assets/{device}/return", new { token }))
            .ReadAsync(HttpStatusCode.OK);

        Assert.Equal(1, returned["availableUnits"]!.GetValue<int>());
        Assert.Equal(api.Clock.GetUtcNow(), returned["returnedAt"]!.GetValue<DateTimeOffset>());
        Assert.False(returned["wasOverdue"]!.GetValue<bool>());

        var again = await admin.PostJson($"/api/v1/assets/{device}/return", new { token });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var unknown = await admin.PostJson($"/api/v1/assets/{device}/return", new { token = "HW-NOPE" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [DatabaseFact]
    public async Task Get_allocations_lists_active_devices_keys_and_credit_for_a_group()
    {
        var category = Steps.NewCategory();
        var device = await Steps.CreatePhysicalAsync(api, category, units: 3);
        var license = await Steps.CreateLicenseAsync(api, category, $"K-{Guid.NewGuid():N}");
        var voucher = await Steps.CreateVoucherAsync(api, category, 50m);
        var ticketId = await Steps.SubmitAsync(api,
            new { assetId = device, quantity = 1 },
            new { assetId = license, quantity = 1 },
            new { assetId = voucher, amount = 20m });
        await Steps.ApproveAsync(api, ticketId);
        var admin = api.As(MockData.Ids.Rivera);
        await (await admin.PostAsync($"/api/v1/assets/tickets/{ticketId}/allocate", null)).ReadAsync(HttpStatusCode.OK);

        var url = $"/api/v1/assets/allocations?courseId={MockData.Ids.CourseCs501}";
        var active = await (await api.As(MockData.Ids.Chen).GetAsync(url)).ReadAsync(HttpStatusCode.OK);
        Assert.Contains(active["devices"]!.AsArray(), d => d!["ticketId"]!.GetValue<string>() == ticketId.ToString());
        Assert.Contains(active["licenseKeys"]!.AsArray(), k => k!["ticketId"]!.GetValue<string>() == ticketId.ToString());
        Assert.Contains(active["vouchers"]!.AsArray(), v => v!["ticketId"]!.GetValue<string>() == ticketId.ToString());

        // Another group's advisor cannot read CS501's allocations (they hold license keys).
        Assert.Equal(HttpStatusCode.Forbidden, (await api.As(MockData.Ids.Raman).GetAsync(url)).StatusCode);

        // Closing the ticket ends the key and voucher allocations; the unreturned device stays listed.
        await (await admin.PutJson($"/api/v1/assets/tickets/{ticketId}", new { status = "closed" })).ReadAsync(HttpStatusCode.OK);
        var afterClose = await (await admin.GetAsync(url)).ReadAsync(HttpStatusCode.OK);
        Assert.Contains(afterClose["devices"]!.AsArray(), d => d!["ticketId"]!.GetValue<string>() == ticketId.ToString());
        Assert.DoesNotContain(afterClose["licenseKeys"]!.AsArray(), k => k!["ticketId"]!.GetValue<string>() == ticketId.ToString());
        Assert.DoesNotContain(afterClose["vouchers"]!.AsArray(), v => v!["ticketId"]!.GetValue<string>() == ticketId.ToString());
    }
}

// Separate class (and database) because it moves the shared test clock forward.
public class OverdueTests : IClassFixture<HubApiFactory>
{
    private readonly HubApiFactory api;

    public OverdueTests(HubApiFactory api)
    {
        this.api = api;
    }

    [DatabaseFact]
    public async Task Device_past_its_due_date_is_overdue()
    {
        var category = Steps.NewCategory();
        var device = await Steps.CreatePhysicalAsync(api, category, units: 1, loanDays: 14);
        var ticketId = await Steps.SubmitAsync(api, new { assetId = device, quantity = 1 });
        await Steps.ApproveAsync(api, ticketId);
        var admin = api.As(MockData.Ids.Rivera);
        var token = (await (await admin.PostAsync($"/api/v1/assets/tickets/{ticketId}/allocate", null))
            .ReadAsync(HttpStatusCode.OK))["devices"]![0]!["token"]!.GetValue<string>();

        api.Clock.Advance(TimeSpan.FromDays(15));

        var overdue = await (await admin.GetAsync($"/api/v1/assets?category={category}&availability=overdue"))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Equal(1, Assert.Single(overdue["items"]!.AsArray())!["physical"]!["overdue"]!.GetValue<int>());

        var active = await (await admin.GetAsync($"/api/v1/assets/allocations?courseId={MockData.Ids.CourseCs501}"))
            .ReadAsync(HttpStatusCode.OK);
        Assert.Equal("overdue", Assert.Single(active["devices"]!.AsArray())!["status"]!.GetValue<string>());

        var returned = await (await admin.PostJson($"/api/v1/assets/{device}/return", new { token }))
            .ReadAsync(HttpStatusCode.OK);
        Assert.True(returned["wasOverdue"]!.GetValue<bool>());
    }
}
